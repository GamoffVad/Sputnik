using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Sputnik;

// Every device owns one snapshot. Vector clocks retain offline deletions and distinguish
// newer changes from simultaneous edits without relying on synchronized system clocks.
internal sealed class FolderSync(string directory)
{
    internal static string Canonical(JsonNode? node)
    {
        JsonNode? Sorted(JsonNode? value) => value switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => new KeyValuePair<string,JsonNode?>(p.Key,Sorted(p.Value)))),
            JsonArray array => new JsonArray(array.Select(Sorted).ToArray()), _ => value?.DeepClone()
        };
        return Sorted(node)?.ToJsonString() ?? "null";
    }
    private static string Hash(JsonNode? node)
    {
        using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(node)))).Replace("-","").ToLowerInvariant();
    }
    internal string Active => new PersonalProfiles(directory).Load()["active"]!.ToString();
    internal JsonObject Configuration => FeatureFiles.Object(directory,"sync-config.json")[Active] as JsonObject ?? new();
    internal void Configure(string folder, bool enabled)
    {
        if (enabled)
        {
            folder = Path.GetFullPath(folder.Trim());
            string local = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((folder.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar).StartsWith(local,StringComparison.OrdinalIgnoreCase) || local.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Выберите отдельную общую папку вне данных приложения.");
            if (!Directory.Exists(folder)) throw new IOException("Общая папка недоступна.");
        }
        var config = FeatureFiles.Object(directory,"sync-config.json");
        string oldFolder=config[Active]?["folder"]?.ToString()??"";
        config[Active] = new JsonObject { ["enabled"] = enabled, ["folder"] = folder };
        var changes=new Dictionary<string,string?>{["sync-config.json"]=config.ToJsonString()};
        if(!string.Equals(oldFolder,folder,StringComparison.OrdinalIgnoreCase))changes["sync-state-"+Active+".json"]=null;
        FeatureFiles.Commit(directory,changes);
    }
    internal JsonObject Collect()
    {
        var workspace = FeatureFiles.Object(directory,"workspace.json"); var records = new JsonObject();
        void Add(string kind,JsonArray items,string idKey)
        {
            foreach(var item in items) records[kind+":"+item![idKey]!.ToString()] = item.DeepClone();
        }
        Add("notes",System.Text.Json.JsonSerializer.SerializeToNode(StickyNotes.Load(directory))!.AsArray(),"Id");
        Add("dates",FeatureFiles.Array(directory,"dates.json"),"id"); Add("reminders",workspace["reminders"] as JsonArray ?? new(),"id");
        Add("favorites",workspace["favorites"] as JsonArray ?? new(),"id"); Add("tasks",FeatureFiles.Array(directory,"tasks.json"),"id");
        ValidateValues(records); return records;
    }
    internal static void ValidateValues(JsonObject values)
    {
        if (values.Count > 16000) throw new InvalidOperationException("Слишком много записей синхронизации.");
        JsonArray Of(string kind) => new(values.Where(p => p.Key.StartsWith(kind+":",StringComparison.Ordinal) && p.Value is not null).Select(p => p.Value!.DeepClone()).ToArray());
        foreach(var entry in values)
        {
            string[] parts = entry.Key.Split(':');
            if(parts.Length!=2 || parts[0] is not ("notes" or "dates" or "reminders" or "favorites" or "tasks") || parts[1].Length is < 1 or > 120 || parts[1].Any(c => char.IsControl(c))) throw new InvalidOperationException("Неизвестная запись синхронизации.");
            if(entry.Value is not null && (entry.Value is not JsonObject || entry.Value[parts[0]=="notes"?"Id":"id"]?.ToString()!=parts[1])) throw new InvalidOperationException("ID синхронизации не совпадает с записью.");
        }
        DataTransfer.Validate("notes",Of("notes"));DataTransfer.Validate("reminders",Of("reminders"));DataTransfer.Validate("dates",Of("dates"));
        var profile = PersonalProfiles.Empty(); profile["favorites"]=Of("favorites");PersonalProfiles.Validate(profile);
        PersonalTasks.Validate(Of("tasks"));
    }
    internal static JsonObject[] ReadPeers(string folder)
    {
        string sync = Path.Combine(folder,"Sputnik-sync-v1");
        if(!Directory.Exists(folder)) throw new IOException("Общая папка недоступна.");
        if(!Directory.Exists(sync)) return [];
        var files = Directory.GetFiles(sync,"*.json"); if(files.Length>100) throw new InvalidDataException("Слишком много устройств в общей папке.");
        var result = new List<JsonObject>();
        foreach(string file in files)
        {
            if(new FileInfo(file).Length>128_000_000) throw new InvalidDataException("Файл синхронизации слишком велик.");
            using var stream = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var reader = new StreamReader(stream);var peer=JsonNode.Parse(reader.ReadToEnd()) as JsonObject ?? throw new InvalidDataException("Повреждён файл синхронизации.");
            ValidateState(peer);result.Add(peer);
        }
        return result.ToArray();
    }
    internal static void ValidateState(JsonObject state)
    {
        if(state["format"]?.ToString()!="sputnik.sync.v1" || !Guid.TryParseExact(state["device"]?.ToString(),"N",out _) || state["records"] is not JsonObject records) throw new InvalidDataException("Неизвестный формат синхронизации.");
        var values = new JsonObject();
        foreach(var entry in records)
        {
            if(entry.Value is not JsonObject item || item["clock"] is not JsonObject clock || clock.Count is < 1 or > 100) throw new InvalidDataException("Повреждена история синхронизации.");
            foreach(var version in clock) if(!Guid.TryParseExact(version.Key,"N",out _) || version.Value!.GetValue<long>() is < 1 or > 9_000_000_000_000_000) throw new InvalidDataException("Повреждена версия записи.");
            values[entry.Key]=item["value"]?.DeepClone();
        }
        ValidateValues(values);
    }
    internal JsonObject Merge(string profile, JsonObject local, IEnumerable<JsonObject> peers)
    {
        string file="sync-state-"+profile+".json";
        var state=FeatureFiles.Object(directory,file);
        if(state.Count==0) state=new JsonObject { ["format"]="sputnik.sync.v1",["device"]=Guid.NewGuid().ToString("N"),["records"]=new JsonObject() };
        ValidateState(state); string device=state["device"]!.ToString();var records=state["records"]!.AsObject();
        foreach(string key in local.Select(p=>p.Key).Union(records.Select(p=>p.Key)).ToArray())
        {
            var previous=records[key] as JsonObject;var value=local[key];
            if(previous is not null && Canonical(previous["value"])==Canonical(value)) continue;
            var clock=previous?["clock"]?.DeepClone() as JsonObject ?? new();clock[device]=(clock[device]?.GetValue<long>()??0)+1;
            records[key]=new JsonObject { ["value"]=value?.DeepClone(),["clock"]=clock };
        }
        foreach(var peer in peers.OrderBy(p=>p["device"]!.ToString(),StringComparer.Ordinal))
        {
            ValidateState(peer);
            foreach(var incoming in peer["records"]!.AsObject())
            {
                if(records[incoming.Key] is not JsonObject current) {records[incoming.Key]=incoming.Value!.DeepClone();continue;}
                var other=incoming.Value!.AsObject();var a=current["clock"]!.AsObject();var b=other["clock"]!.AsObject();
                bool newer=false,older=false;
                foreach(string key in a.Select(p=>p.Key).Union(b.Select(p=>p.Key)))
                {long av=a[key]?.GetValue<long>()??0,bv=b[key]?.GetValue<long>()??0;newer|=av>bv;older|=av<bv;}
                if(!older) continue;
                if(!newer) {records[incoming.Key]=other.DeepClone();continue;}
                var merged=a.DeepClone().AsObject();foreach(var p in b) merged[p.Key]=Math.Max(merged[p.Key]?.GetValue<long>()??0,p.Value!.GetValue<long>());
                JsonNode? winner=current["value"],loser=other["value"];
                if(winner is null || loser is not null && string.CompareOrdinal(Hash(loser),Hash(winner))>0) (winner,loser)=(loser,winner);
                if(loser is not null && Canonical(winner)!=Canonical(loser))
                {
                    string kind=incoming.Key.Split(':')[0],original=incoming.Key.Split(':')[1];
                    string copy=(original.Length>65?original.Substring(0,65):original)+"-conflict-"+Hash(loser).Substring(0,20);string copyKey=kind+":"+copy;
                    if(!records.ContainsKey(copyKey))
                    {
                        var preserved=loser.DeepClone();preserved[kind=="notes"?"Id":"id"]=copy;
                        foreach(string title in new[]{"title","name"}) if(preserved[title] is {} label) preserved[title]=(label.ToString().Length>100?label.ToString().Substring(0,100):label.ToString())+" (конфликт)";
                        records[copyKey]=new JsonObject{["value"]=preserved,["clock"]=merged.DeepClone()};
                    }
                }
                records[incoming.Key]=new JsonObject{["value"]=winner?.DeepClone(),["clock"]=merged};
            }
        }
        // A deleted note cannot leave an active link behind on another computer.
        foreach(var entry in records.ToArray())
            if(entry.Key.StartsWith("reminders:",StringComparison.Ordinal) && entry.Value?["value"]?["noteId"]?.ToString() is {Length:>0} id && records["notes:"+id] is {} note && note["value"] is null)
                entry.Value!["value"]=null;
        ValidateState(state);return state;
    }
    internal bool Apply(string profile,JsonObject state)
    {
        if(Active!=profile) throw new InvalidOperationException("Активный профиль изменился.");
        ValidateState(state);var values=new JsonObject();foreach(var entry in state["records"]!.AsObject()) values[entry.Key]=entry.Value!["value"]?.DeepClone();
        var before=Collect();bool changed=Canonical(before)!=Canonical(new JsonObject(values.Where(p=>p.Value is not null).Select(p=>new KeyValuePair<string,JsonNode?>(p.Key,p.Value!.DeepClone()))));
        var changes=new Dictionary<string,string?> { ["sync-state-"+profile+".json"]=state.ToJsonString() };
        if(changed)
        {
            string backups=Path.Combine(directory,"sync-backups");Directory.CreateDirectory(backups);FeatureFiles.Write(backups,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json",before);
            JsonArray Of(string kind)=>new(values.Where(p=>p.Key.StartsWith(kind+":",StringComparison.Ordinal)&&p.Value is not null).Select(p=>p.Value!.DeepClone()).ToArray());
            var workspace=FeatureFiles.Object(directory,"workspace.json");workspace["favorites"]=Of("favorites");workspace["reminders"]=Of("reminders");
            changes["workspace.json"]=workspace.ToJsonString();changes["notes.json"]=Of("notes").ToJsonString();changes["dates.json"]=Of("dates").ToJsonString();changes["tasks.json"]=Of("tasks").ToJsonString();
            var modifiedDates=before.Where(p=>p.Key.StartsWith("dates:",StringComparison.Ordinal) && Canonical(p.Value)!=Canonical(values[p.Key])).Select(p=>p.Key.Substring(6)).ToArray();
            if(modifiedDates.Length>0)
            {
                var inbox=new NotificationInbox(directory).Load();
                foreach(var item in inbox.Where(i=>i?["done"]?.GetValue<bool>()!=true && modifiedDates.Any(id=>i?["id"]?.ToString().StartsWith("date:"+id+":",StringComparison.Ordinal)==true)).ToArray())inbox.Remove(item);
                changes["notifications.json"]=inbox.ToJsonString();
            }
        }
        FeatureFiles.Commit(directory,changes);if(changed) DataTransfer.SyncAll(directory);return changed;
    }
    internal static void Publish(string folder,JsonObject state)
    {
        ValidateState(state);string sync=Path.Combine(folder,"Sputnik-sync-v1");
        if(!Directory.Exists(folder)) throw new IOException("Общая папка недоступна.");Directory.CreateDirectory(sync);
        FeatureFiles.Write(sync,state["device"]!.ToString()+".json",state);
    }
}
