using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Schema;
using Npgsql;

namespace Marten.V4Internals
{
    public abstract class IdentityMapDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public IdentityMapDocumentStorage(DocumentMapping document) : base(document)
        {
        }

        public sealed override void Eject(IMartenSession session, T document)
        {
            var id = Identity(document);
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d.Remove(id);
                }
            }
        }

        public sealed override void Store(IMartenSession session, T document)
        {
            var id = AssignIdentity(document, session.Tenant);
            session.MarkAsAddedForStorage(id, document);

            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    if (d.ContainsKey(id))
                    {
                        throw new InvalidOperationException($"Document '{typeof(T).FullNameInCode()}' with same Id already added to the session.");
                    }

                    d[id] = document;
                }
                else
                {
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(T)}");
                }
            }
            else
            {
                var dict = new Dictionary<TId, T> {{id, document}};
                session.ItemMap.Add(typeof(T), dict);
            }
        }

        public sealed override void Store(IMartenSession session, T document, Guid? version)
        {
            var id = AssignIdentity(document, session.Tenant);
            session.MarkAsAddedForStorage(id, document);

            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d[id] = document;
                }
                else
                {
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(T)}");
                }
            }
            else
            {
                var dict = new Dictionary<TId, T> {{id, document}};
                session.ItemMap.Add(typeof(T), dict);
            }

            if (version != null)
            {
                session.Versions.StoreVersion<T, TId>(id, version.Value);
            }
            else
            {
                session.Versions.ClearVersion<T, TId>(id);
            }
        }

        public sealed override IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session)
        {
            var list = preselectLoadedDocuments(ids, session, out var command);
            var selector = (ISelector<T>)BuildSelector(session);

            using (var reader = session.Database.ExecuteReader(command))
            {
                while (reader.Read())
                {
                    var document = selector.Resolve(reader);
                    list.Add(document);
                }
            }

            return list;
        }

        private List<T> preselectLoadedDocuments(TId[] ids, IMartenSession session, out NpgsqlCommand command)
        {
            var list = new List<T>();

            Dictionary<TId, T> dict;
            if (session.ItemMap.TryGetValue(typeof(T), out var d))
            {
                dict = (Dictionary<TId, T>) d;
            }
            else
            {
                dict = new Dictionary<TId, T>();
                session.ItemMap.Add(typeof(TId), dict);
            }

            var idList = new List<TId>();
            foreach (var id in ids)
            {
                if (dict.TryGetValue(id, out var doc))
                {
                    list.Add(doc);
                }
                else
                {
                    idList.Add(id);
                }
            }

            command = BuildLoadManyCommand(idList.ToArray(), session.Tenant);
            return list;
        }

        public sealed override async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session,
            CancellationToken token)
        {
            var list = preselectLoadedDocuments(ids, session, out var command);
            var selector = (ISelector<T>)BuildSelector(session);

            using (var reader = await session.Database.ExecuteReaderAsync(command, token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var document = await selector.ResolveAsync(reader, token).ConfigureAwait(false);
                    list.Add(document);
                }
            }

            return list;
        }

        public sealed override T Load(TId id, IMartenSession session)
        {
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    if (d.TryGetValue(id, out var item)) return item;
                }
                else
                {
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(T)}");
                }
            }

            return load(id, session);
        }

        public sealed override async Task<T> LoadAsync(TId id, IMartenSession session, CancellationToken token)
        {
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    if (d.TryGetValue(id, out var item)) return item;
                }
                else
                {
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(T)}");
                }
            }

            return await loadAsync(id, session, token);
        }
    }
}