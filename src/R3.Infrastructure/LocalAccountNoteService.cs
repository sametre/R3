using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record AccountNoteEdit(string Id, string AccountId, string NoteType, string Title, string Content, bool IsPinned, string CreatedBy);

/// <summary>Account 1 -> N AccountNote. Notes are never physically deleted, only left as-is;
/// pinned notes sort first (see List).</summary>
public sealed class LocalAccountNoteService(StoreDatabase database)
{
    private static readonly string[] ValidTypes = ["Genel", "Satış", "Finans", "Tahsilat", "Risk", "Şikayet", "Diğer"];

    public DataTable List(string accountId) => database.Query("""
        SELECT id AS Id, note_type AS Tip, title AS Baslik, content AS Icerik, created_by AS Kullanici,
               created_at AS Tarih, is_pinned AS Sabit
        FROM account_notes WHERE account_id=$account ORDER BY is_pinned DESC, created_at DESC
        """, ("$account", accountId));

    public void Save(AccountNoteEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.AccountId)) throw new ArgumentException("Cari seçilmelidir.");
        if (string.IsNullOrWhiteSpace(edit.Title)) throw new ArgumentException("Not başlığı zorunludur.");
        if (string.IsNullOrWhiteSpace(edit.Content)) throw new ArgumentException("Not içeriği zorunludur.");
        if (!ValidTypes.Contains(edit.NoteType)) throw new ArgumentException("Geçersiz not tipi.");
        var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = Open(); using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO account_notes(id,account_id,note_type,title,content,is_pinned,created_by,created_at,updated_at)
            VALUES($id,$account,$type,$title,$content,$pinned,$by,$now,$now)
            ON CONFLICT(id) DO UPDATE SET note_type=$type,title=$title,content=$content,is_pinned=$pinned,updated_at=$now
            """;
        Add(cmd, "$id", id); Add(cmd, "$account", edit.AccountId); Add(cmd, "$type", edit.NoteType); Add(cmd, "$title", edit.Title.Trim());
        Add(cmd, "$content", edit.Content.Trim()); Add(cmd, "$pinned", edit.IsPinned ? 1 : 0); Add(cmd, "$by", edit.CreatedBy); Add(cmd, "$now", now);
        cmd.ExecuteNonQuery();
        Audit(c, tx, edit.AccountId, string.IsNullOrWhiteSpace(edit.Id) ? "NoteAdded" : "NoteUpdated", id, now);
        tx.Commit();
    }

    public void TogglePinned(string id, bool pinned)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var read = c.CreateCommand(); read.Transaction = tx; read.CommandText = "SELECT account_id FROM account_notes WHERE id=$id"; Add(read, "$id", id);
        string accountId;
        using (var r = read.ExecuteReader()) { if (!r.Read()) throw new KeyNotFoundException("Not bulunamadı."); accountId = r.GetString(0); }
        var now = DateTime.UtcNow.ToString("O");
        using var upd = c.CreateCommand(); upd.Transaction = tx; upd.CommandText = "UPDATE account_notes SET is_pinned=$pinned, updated_at=$now WHERE id=$id";
        Add(upd, "$pinned", pinned ? 1 : 0); Add(upd, "$now", now); Add(upd, "$id", id); upd.ExecuteNonQuery();
        Audit(c, tx, accountId, "NoteUpdated", id, now);
        tx.Commit();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string accountId, string action, string noteId, string now)
    {
        using var audit = c.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO audit_logs(id,entity_type,entity_id,action,new_values,created_at) VALUES($id,'AccountNote',$entity,$action,$new,$now)";
        Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$entity", noteId); Add(audit, "$action", action); Add(audit, "$new", accountId); Add(audit, "$now", now);
        audit.ExecuteNonQuery();
    }

    private SqliteConnection Open() => database.OpenConnection();
    private static void Add(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
}
