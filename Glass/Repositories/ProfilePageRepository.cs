using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProfilePageRepository
//
// Provides access to the ProfilePages table.
// Manages the association between profiles and key pages,
// including which page is the start page per device.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ProfilePageRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetPagesForProfile
    //
    // Returns all key pages associated with the given profile,
    // including whether each is the start page.
    //
    // characterSetId:  The profile to query
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<ProfilePage> GetPagesForProfile(int characterSetId)
    {
        DebugLog.Write(DebugLog.Log_Database, $"ProfilePageRepository.GetPagesForProfile: characterSetId={characterSetId}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pp.id, pp.character_set_id, pp.key_page_id, pp.is_start_page,
                   kp.name, kp.device
            FROM ProfilePages pp
            JOIN KeyPages kp ON kp.id = pp.key_page_id
            WHERE pp.character_set_id = @characterSetId
            ORDER BY kp.device, kp.name";
        cmd.Parameters.AddWithValue("@characterSetId", characterSetId);

        var pages = new List<ProfilePage>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            pages.Add(new ProfilePage
            {
                Id = reader.GetInt32(0),
                CharacterSetId = reader.GetInt32(1),
                KeyPageId = reader.GetInt32(2),
                IsStartPage = reader.GetInt32(3) != 0,
                PageName = reader.GetString(4),
                Device = reader.GetString(5).ToKeyboardType()
            });
        }

        DebugLog.Write(DebugLog.Log_Database, $"ProfilePageRepository.GetPagesForProfile: found {pages.Count} pages.");
        return pages;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetPagesForProfile
    //
    // Replaces all page associations for the given profile with the provided list.
    // Runs in a transaction.
    //
    // characterSetId:  The profile to update
    // pages:           The new set of page associations
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetPagesForProfile(int characterSetId, List<ProfilePage> pages)
    {
        DebugLog.Write(DebugLog.Log_Database, $"ProfilePageRepository.SetPagesForProfile: characterSetId={characterSetId} count={pages.Count}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            using var delete = conn.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM ProfilePages WHERE character_set_id = @characterSetId";
            delete.Parameters.AddWithValue("@characterSetId", characterSetId);
            delete.ExecuteNonQuery();

            foreach (var page in pages)
            {
                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = @"
                    INSERT INTO ProfilePages (character_set_id, key_page_id, is_start_page)
                    VALUES (@characterSetId, @keyPageId, @isStartPage)";
                insert.Parameters.AddWithValue("@characterSetId", characterSetId);
                insert.Parameters.AddWithValue("@keyPageId", page.KeyPageId);
                insert.Parameters.AddWithValue("@isStartPage", page.IsStartPage ? 1 : 0);
                insert.ExecuteNonQuery();
            }

            tx.Commit();
            DebugLog.Write(DebugLog.Log_Database, $"ProfilePageRepository.SetPagesForProfile: committed.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(DebugLog.Log_Database, $"ProfilePageRepository.SetPagesForProfile: exception: {ex.Message}, rolling back.");
            throw;
        }
    }
}