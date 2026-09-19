namespace R3.Infrastructure;

public sealed class UserGridLayoutService(StoreDatabase database, string userName)
{
    public string? Load(string viewKey) => database.Query("""
        SELECT l.layout_json FROM user_grid_layouts l JOIN users u ON u.id=l.user_id
        WHERE u.username=$user AND l.view_key=$view LIMIT 1
        """, ("$user", (object)userName), ("$view", viewKey)).Rows.Cast<System.Data.DataRow>().FirstOrDefault()?[0]?.ToString();

    public void Save(string viewKey, string layoutJson, bool isDefault = false)
    {
        var now = DateTime.UtcNow.ToString("O");
        database.Execute("""
            INSERT INTO user_grid_layouts(id,user_id,view_key,layout_json,is_default,created_at,updated_at)
            SELECT $id,u.id,$view,$json,$default,$now,$now FROM users u WHERE u.username=$user
            ON CONFLICT(user_id,view_key) DO UPDATE SET layout_json=$json,is_default=$default,updated_at=$now
            """, ("$id", Guid.NewGuid().ToString()), ("$view", viewKey), ("$json", layoutJson), ("$default", isDefault ? 1 : 0), ("$now", now), ("$user", userName));
    }

    public void Reset(string viewKey) => database.Execute("DELETE FROM user_grid_layouts WHERE user_id=(SELECT id FROM users WHERE username=$user) AND view_key=$view", ("$user", (object)userName), ("$view", viewKey));
}
