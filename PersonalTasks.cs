using System.Text.Json.Nodes;

namespace Sputnik;

internal sealed class PersonalTasks(string directory)
{
    internal JsonArray Load() {var items=FeatureFiles.Array(directory,"tasks.json");Validate(items);return items;}
    internal void Add(string title)
    {
        title=title.Trim();if(title.Length is < 1 or > 200)throw new InvalidOperationException("Задача — от 1 до 200 символов.");
        var items=Load();items.Add(new JsonObject{["id"]=Guid.NewGuid().ToString("N"),["title"]=title,["done"]=false});Save(items);
    }
    internal void Change(string id,bool delete=false)
    {
        var items=Load();var item=items.FirstOrDefault(i=>i!["id"]!.ToString()==id);if(item is null)return;
        if(delete)items.Remove(item);else item["done"]=item["done"]?.GetValue<bool>()!=true;Save(items);
    }
    private void Save(JsonArray items){Validate(items);FeatureFiles.Write(directory,"tasks.json",items);}
    internal static void Validate(JsonArray items)
    {
        var ids=new HashSet<string>();if(items.Count>5000)throw new InvalidOperationException("Можно сохранить до 5000 задач.");
        foreach(var item in items)if(item is not JsonObject || item["id"]?.ToString() is not {Length:>0 and <=120} id || !ids.Add(id) || item["title"]?.ToString().Trim().Length is not (>0 and <=200) || item["done"] is not System.Text.Json.Nodes.JsonValue done || !done.TryGetValue<bool>(out _))throw new InvalidOperationException("Повреждены данные задач.");
    }
}
