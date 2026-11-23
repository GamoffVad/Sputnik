using System.IO;
using System.Text.Json.Nodes;

namespace Sputnik;

// Each request captures its chat ID; switching/deleting a conversation cannot
// redirect a late answer into another conversation or resurrect a deleted one.
internal sealed class ChatHistory(string directory)
{
    private const string FileName = "chats.json";
    internal JsonObject Load()
    {
        if (FeatureFiles.Read(directory, FileName) is JsonObject saved) return saved;
        var workspace = FeatureFiles.Object(directory, "workspace.json");
        var messages = workspace["messages"] as JsonArray ?? new();
        var state = new JsonObject { ["version"] = 1, ["chats"] = new JsonArray() };
        if (messages.Count > 0)
        {
            var chat = MakeChat(); chat["title"] = "Предыдущая переписка";
            chat["messages"] = messages.DeepClone();
            state["chats"]!.AsArray().Add(chat); state["active"] = chat["id"]!.ToString();
        }
        workspace.Remove("messages");
        FeatureFiles.Commit(directory, new Dictionary<string, string?>
        {
            [FileName] = state.ToJsonString(), ["workspace.json"] = workspace.ToJsonString()
        });
        return state;
    }

    internal JsonObject? Active()
    {
        var state = Load();
        return Rows(state).OfType<JsonObject>().FirstOrDefault(c => c["id"]?.ToString() == state["active"]?.ToString());
    }
    internal string Create()
    {
        var state = Load(); var current = Rows(state).OfType<JsonObject>().FirstOrDefault(c => c["id"]?.ToString() == state["active"]?.ToString());
        if (current?["messages"] is JsonArray { Count: 0 }) return current["id"]!.ToString();
        var chat = MakeChat(); string id = chat["id"]!.ToString();
        Rows(state).Add(chat); state["active"] = id; Save(state); return id;
    }
    internal void Select(string id)
    {
        var state = Load();
        if (!Rows(state).Any(c => c?["id"]?.ToString() == id)) throw new InvalidOperationException("Чат удалён или не найден.");
        state["active"] = id; Save(state);
    }
    internal void Delete(string id)
    {
        var state = Load(); var chat = Rows(state).FirstOrDefault(c => c?["id"]?.ToString() == id);
        if (chat is null) return;
        Rows(state).Remove(chat);
        if (state["active"]?.ToString() == id) state["active"] = null;
        Save(state);
    }
    internal JsonArray? Append(string id, string role, string text)
    {
        var state = Load(); var chat = Rows(state).OfType<JsonObject>().FirstOrDefault(c => c["id"]?.ToString() == id);
        if (chat is null) return null;
        var messages = chat["messages"]!.AsArray();
        messages.Add(new JsonObject { ["role"] = role, ["content"] = text, ["at"] = DateTimeOffset.UtcNow.ToString("O") });
        if (role == "user" && messages.Count == 1) chat["title"] = Title(text);
        chat["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"); Save(state);
        return messages;
    }
    private void Save(JsonObject state) => FeatureFiles.Write(directory, FileName, state);
    private static JsonArray Rows(JsonObject state) => state["chats"]!.AsArray();
    private static JsonObject MakeChat()
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        return new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["title"] = "Новый чат", ["createdAt"] = now, ["updatedAt"] = now, ["messages"] = new JsonArray() };
    }
    internal static string Title(string text)
    {
        string title = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length > 70 ? title[..69] + "…" : title;
    }
}
