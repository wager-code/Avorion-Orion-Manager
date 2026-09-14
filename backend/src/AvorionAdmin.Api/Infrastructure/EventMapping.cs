namespace AvorionAdmin.Api;

public static class EventMapping
{
    public static string ToEventKind(string category) => category switch
    {
        "Player" => "player",
        "Save" => "save",
        "RCON" => "rcon",
        "Warning" or "Error" => "warning",
        _ => "other"
    };
}
