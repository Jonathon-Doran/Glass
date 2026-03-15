using Glass.Data;
using Glass.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Glass.Data.Repositories;

public class CharacterRepository
{
    public List<Character> GetAll()
    {
        var characters = new List<Character>();
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, class, account_id, progression, server FROM Characters ORDER BY account_id, name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            characters.Add(new Character
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Class = (EQClass)reader.GetInt32(2),
                AccountId = reader.GetInt32(3),
                Progression = reader.GetInt32(4) != 0,
                Server = reader.GetString(5)
            });
        }

        return characters;
    }
}
