using System;
using System.Collections.Generic;

namespace Mimas.Core.Content
{
    /// <summary>Anything the catalogue stores by id. Ids are lowercase kebab-case and unique within their table.</summary>
    public interface IContentDef
    {
        string Id { get; }
    }

    /// <summary>
    /// An immutable, id-keyed table of one content type. Entries are sorted by ordinal id and numbered
    /// from 0, so <see cref="IndexOf"/> gives a stable small integer for wire encoding that every peer
    /// loading the same content derives identically (never a hash of the id: hashes collide).
    /// </summary>
    public sealed class DefinitionTable<T> where T : class, IContentDef
    {
        public static readonly DefinitionTable<T> Empty = new DefinitionTable<T>(new List<T>(0));

        private readonly List<T> _ordered;
        private readonly Dictionary<string, int> _indexById;

        /// <summary>Entries in id order.</summary>
        public IReadOnlyList<T> All => _ordered;

        public int Count => _ordered.Count;

        /// <summary>Builds the table; the caller guarantees ids are unique (the catalogue checks first).</summary>
        public DefinitionTable(List<T> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            _ordered = new List<T>(entries);
            _ordered.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            _indexById = new Dictionary<string, int>(_ordered.Count, StringComparer.Ordinal);
            for (int i = 0; i < _ordered.Count; i++)
            {
                if (_indexById.ContainsKey(_ordered[i].Id))
                    throw new ArgumentException($"Duplicate {typeof(T).Name} id '{_ordered[i].Id}'.", nameof(entries));
                _indexById[_ordered[i].Id] = i;
            }
        }

        public bool Contains(string id) => id != null && _indexById.ContainsKey(id);

        public bool TryGet(string id, out T def)
        {
            int index;
            if (id != null && _indexById.TryGetValue(id, out index))
            {
                def = _ordered[index];
                return true;
            }
            def = null;
            return false;
        }

        /// <summary>Throws a message that names the type and the table size, so a typo is obvious in a log.</summary>
        public T Get(string id)
        {
            T def;
            if (TryGet(id, out def)) return def;
            throw new KeyNotFoundException($"No {typeof(T).Name} with id '{id}' ({_ordered.Count} loaded).");
        }

        /// <summary>Stable position of the id in this table, or -1.</summary>
        public int IndexOf(string id)
        {
            int index;
            return id != null && _indexById.TryGetValue(id, out index) ? index : -1;
        }

        public T ByIndex(int index)
        {
            if (index < 0 || index >= _ordered.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _ordered[index];
        }
    }
}
