// 
// Copyright SmartFormat Project maintainers and contributors.
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using SmartFormat.Core.Extensions;

namespace SmartFormat.Extensions;

/// <summary>
/// Class to evaluate sources of types <see cref="IDictionary"/>,
/// generic <see cref="IDictionary{TKey,TValue}"/>, dynamic <see cref="System.Dynamic.ExpandoObject"/>,
/// and <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
/// Include this source, if any of these types shall be used.
/// <para/>
/// For support of <see cref="IReadOnlyDictionary{TKey,TValue}"/>, <see cref="IsIReadOnlyDictionarySupported"/> must be set to <see langword="true"/>.
/// This uses Reflection and is slower than the other types despite caching.
/// The cache scope is limited to this instance of <see cref="DictionarySource"/>.
/// </summary>
public class DictionarySource : Source
{
    /// <inheritdoc />
    public override bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        var current = selectorInfo.CurrentValue;
        if (TrySetResultForNullableOperator(selectorInfo)) return true;

        if (current == null) return false;
        var dictionaryType = GetDictionaryType(current);
        if (dictionaryType == DictionaryType.None) return false;

        var selector = selectorInfo.SelectorText;
        var comparison = selectorInfo.FormatDetails.Settings.GetCaseSensitivityComparison();

        // Try to get the selector value for IDictionary
        if (dictionaryType == DictionaryType.NonGeneric
            && TryGetIDictionaryValue(current, selector, comparison, out var value))
        {
            selectorInfo.Result = value;
            return true;
        }

        // Try to get the selector value for Dictionary<,> and dynamics (ExpandoObject)
        if (dictionaryType == DictionaryType.Generic
            && TryGetGenericDictionaryValue(current, selector, comparison, out value))
        {
            selectorInfo.Result = value;
            return true;
        }

        // Try to get the selector value for IReadOnlyDictionary<,>
        if (dictionaryType == DictionaryType.ReadOnly && IsIReadOnlyDictionarySupported
            && TryGetReadOnlyDictionaryValue(current, selector, comparison, out value))
        {
            selectorInfo.Result = value;
            return true;
        }

        // No matching key found in a dictionary, but if the selector
        // has a nullable operator, we set the result to null
        // instead of leaving it as not found.
        if (HasNullableOperator(selectorInfo))
        {
            selectorInfo.Result = null;
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// See if <paramref name="current"/> is an IDictionary (including generic dictionaries) that contains the selector.
    /// </summary>
    private static bool TryGetIDictionaryValue(object current, string selectorText, StringComparison comparison, out object? value)
    {
        if (current is IDictionary rawDict)
            foreach (DictionaryEntry entry in rawDict)
            {
                var key = entry.Key as string ?? entry.Key.ToString()!;

                if (!key.Equals(selectorText, comparison))
                    continue;

                value = entry.Value;
                return true;
            }

        value = null;
        return false;
    }

    /// <summary>
    /// Try to get the selector value for <see cref="Dictionary{TKey,TValue}"/> and dynamics (<see cref="System.Dynamic.ExpandoObject"/>).
    /// </summary>
    private static bool TryGetGenericDictionaryValue(object current, string selectorText, StringComparison comparison,
        out object? value)
    {
        if (current is IDictionary<string, object?> dict)
            foreach (var entry in dict)
            {
                var key = entry.Key;

                if (!key.Equals(selectorText, comparison))
                    continue;

                value = entry.Value;
                return true;
            }

        value = null;
        return false;
    }
    
    #region *** IReadOnlyDictionary<,> ***

    /// <summary>
    /// Gets the instance type cache <see cref="IDictionary{TKey,TValue}"/> for <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
    /// It could e.g. be pre-filled or cleared in a derived class.
    /// The cache scope is limited to this instance of <see cref="DictionarySource"/>.
    /// </summary>
    protected internal readonly IDictionary<Type, (PropertyInfo, PropertyInfo)?> RoDictionaryTypeCache =
        new Dictionary<Type, (PropertyInfo, PropertyInfo)?>();

    /// <summary>
    /// Gets or sets, whether the <see cref="IReadOnlyDictionary{TKey,TValue}"/> interface should be supported.
    /// Although caching is used, this is still slower than the other types.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool IsIReadOnlyDictionarySupported { get; set; } = false;
    
    private bool TryGetReadOnlyDictionaryValue(object obj, string key, StringComparison comparison, out object? value)
    {
        value = null;

        if (!TryGetDictionaryProperties(obj.GetType(), out var propertyTuple)) return false;

        var keys = (IEnumerable) propertyTuple!.Value.KeyProperty.GetValue(obj)!;

        foreach (var k in keys)
        {
            if (!k.ToString()!.Equals(key, comparison))
                continue;

            value = propertyTuple.Value.ItemProperty.GetValue(obj, new [] { k });
            return true;
        }

        return false;
    }

    private bool TryGetDictionaryProperties(Type type, out (PropertyInfo KeyProperty, PropertyInfo ItemProperty)? propertyTuple)
    {
        // try to get the properties from the cache
        if (RoDictionaryTypeCache.TryGetValue(type, out propertyTuple))
            return propertyTuple != null;

        // get Key and Item properties of the dictionary
        propertyTuple = (type.GetProperty(nameof(IDictionary.Keys)), type.GetProperty("Item"))!;

        System.Diagnostics.Debug.Assert(propertyTuple.Value.KeyProperty != null && propertyTuple.Value.ItemProperty != null, "Key and Item properties must not be null");

        RoDictionaryTypeCache[type] = propertyTuple;
        return true;
    }

    private static readonly ConcurrentDictionary<Type, bool> RoDictionaryTypeBoolCache = new();

    private static bool IsIReadOnlyDictionary(Type type)
    {
        if (RoDictionaryTypeBoolCache.TryGetValue(type, out var cached))
            return cached;

        // No Linq for less garbage
        var result = false;
        foreach (var typeInterface in type.GetInterfaces())
        {
            if (typeInterface == typeof(IReadOnlyDictionary<,>) ||
                (typeInterface.IsGenericType
                 && typeInterface.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
            {
                result = true;
                break;
            }
        }

        RoDictionaryTypeBoolCache[type] = result;
        return result;
    }

    #endregion

    internal enum DictionaryType
    {
        None,
        NonGeneric,
        Generic,
        ReadOnly
    }

    private DictionaryType GetDictionaryType(object current)
    {
        if (current is IDictionary) return DictionaryType.NonGeneric;
        if (current is IDictionary<string, object?>) return DictionaryType.Generic;
        if (IsIReadOnlyDictionarySupported && IsIReadOnlyDictionary(current.GetType())) return DictionaryType.ReadOnly;
        return DictionaryType.None;
    }
}
