using System.Collections.Generic;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Список играбельных персонажей. По сети и в сохранениях персонаж передаётся по <see cref="FighterDefinition.Id"/>
    /// (ссылку на ассет не передашь), здесь Id превращается обратно в ассет. Ассет: Resources/Fighters/Roster.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Combat/Fighter Roster", fileName = "Roster")]
    public class FighterRoster : ScriptableObject
    {
        public const string ResourcePath = "Fighters/Roster";

        public List<FighterDefinition> Fighters = new();

        /// <summary> Персонаж по Id; null — такого нет. </summary>
        public FighterDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var f in Fighters)
                if (f != null && f.Id == id) return f;
            return null;
        }

        /// <summary> Ошибки ростера: пустые строки, повторы, совпадающие Id. </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            var seen = new HashSet<FighterDefinition>();
            for (int i = 0; i < Fighters.Count; i++)
            {
                var f = Fighters[i];
                if (f == null) { problems.Add($"Строка {i}: пусто."); continue; }
                if (!seen.Add(f)) problems.Add($"«{f.name}» в списке дважды.");
                else if (!string.IsNullOrEmpty(f.Id) && !ids.Add(f.Id)) problems.Add($"Id «{f.Id}» повторяется («{f.name}»).");
            }
            return problems;
        }

        public static FighterRoster Load() => Resources.Load<FighterRoster>(ResourcePath);
    }
}
