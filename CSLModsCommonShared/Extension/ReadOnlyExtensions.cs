using CSLModsCommon.Collections;
using System.Collections.Generic;

namespace CSLModsCommon.Extension; 
public static class ReadOnlyExtensions {
    public static IReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(this IDictionary<TKey, TValue> dict) => new ReadOnlyDictionaryWrapper<TKey, TValue>(dict);

    public static IReadOnlyList<T> AsReadOnly<T>(this IList<T> list) => new ReadOnlyListWrapper<T>(list);

    public static IReadOnlyCollection<T> AsReadOnly<T>(this ICollection<T> collection) => new ReadOnlyCollectionWrapper<T>(collection);
}