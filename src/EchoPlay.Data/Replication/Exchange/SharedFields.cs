using System.Collections.Concurrent;
using System.Reflection;
using EchoPlay.Core.Replication.Exchange;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;

namespace EchoPlay.Data.Replication.Exchange
{
    /// <summary>
    /// Die gemeinsamen Felder einer Entität, wie die Einteilung je Feld (<see cref="DataScope.Shared"/>)
    /// sie festlegt, und ihr Weg in Textform und zurück.
    /// </summary>
    /// <remarks>
    /// Die Felder von <see cref="BaseEntity"/> gehören nicht dazu: Löschung und Zeitpunkt trägt der
    /// Satz selbst, Anlage- und Änderungszeit sind je Gerät verschieden. Kommt ein neues gemeinsames
    /// Feld dazu, gleicht es ohne weitere Änderung mit ab.
    /// </remarks>
    internal static class SharedFields
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

        /// <summary>Die gemeinsamen Felder eines Typs, nach Namen sortiert.</summary>
        /// <param name="type">Der Entitätstyp.</param>
        /// <returns>Die Eigenschaften.</returns>
        public static IReadOnlyList<PropertyInfo> Of(Type type) => Cache.GetOrAdd(type, static t => [.. t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType != typeof(BaseEntity) && p.CanWrite)
            .Where(p => p.GetCustomAttribute<DataScopeAttribute>()?.Scope == DataScope.Shared)
            .OrderBy(p => p.Name, StringComparer.Ordinal)]);

        /// <summary>Liest die gemeinsamen Felder einer Entität als Text.</summary>
        /// <param name="entity">Die Entität.</param>
        /// <returns>Name und Wert je Feld.</returns>
        public static Dictionary<string, string?> Read(object entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Of(entity.GetType()).ToDictionary(p => p.Name, p => FieldText.Format(p.GetValue(entity)), StringComparer.Ordinal);
        }

        /// <summary>
        /// Schreibt Felder aus einem Satz in eine Entität. Ein Feld, das der Typ nicht kennt oder dessen
        /// Text nicht zum Typ passt, bleibt unverändert und wird gemeldet.
        /// </summary>
        /// <param name="entity">Die Entität.</param>
        /// <param name="fields">Die Felder aus dem Satz.</param>
        /// <returns>Die Namen der Felder, die nicht übernommen werden konnten.</returns>
        public static List<string> Write(object entity, IReadOnlyDictionary<string, string?> fields)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(fields);
            List<string> rejected = [];
            Dictionary<string, PropertyInfo> known = Of(entity.GetType()).ToDictionary(p => p.Name, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string?> field in fields)
            {
                if (known.TryGetValue(field.Key, out PropertyInfo? property)
                    && FieldText.TryParse(field.Value, property.PropertyType, out object? value)
                    && (value is not null || !IsRequiredText(property)))
                {
                    property.SetValue(entity, value);
                }
                else
                {
                    rejected.Add(field.Key);
                }
            }

            return rejected;
        }

        /// <summary>Ein Text, der nicht leer sein darf (nicht nullbar deklariert).</summary>
        private static bool IsRequiredText(PropertyInfo property)
            => property.PropertyType == typeof(string)
                && new NullabilityInfoContext().Create(property).WriteState == NullabilityState.NotNull;
    }
}
