using System.Collections.Concurrent;
using System.Collections.Generic;


namespace RakutenDrive.Utils;

/// <summary>
///     A thread-safe implementation of a set that leverages a concurrent dictionary
///     for storing unique items of type T.
/// </summary>
/// <typeparam name="T">
///     The type of elements in the set. This type must be non-nullable.
/// </typeparam>
public class ConcurrentSet<T> where T : notnull
{
	private readonly ConcurrentDictionary<T, bool> _dictionary = new();
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------

	public int Count => _dictionary.Count;


	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------

	/// <summary>
	///     Adds the specified item to the set if it is not already present.
	/// </summary>
	/// <param name="item">The item to be added to the set.</param>
	/// <returns>
	///     True if the item was successfully added to the set; false if the item
	///     was already present in the set.
	/// </returns>
	public bool Add(T item)
	{
		return _dictionary.TryAdd(item, true);
	}


	/// <summary>
	///     Determines whether the set contains the specified item.
	/// </summary>
	/// <param name="item">The item to locate in the set.</param>
	/// <returns>
	///     True if the item is found in the set; otherwise, false.
	/// </returns>
	public bool Contains(T item)
	{
		return _dictionary.ContainsKey(item);
	}


	/// <summary>
	///     Removes the specified item from the set if it exists.
	/// </summary>
	/// <param name="item">The item to be removed from the set.</param>
	/// <returns>
	///     True if the item was successfully removed from the set; false if the item
	///     was not found in the set.
	/// </returns>
	public bool Remove(T item)
	{
		return _dictionary.TryRemove(item, out _); // Discard the value
	}


	/// <summary>
	///     Removes all items from the set.
	/// </summary>
	public void Clear()
	{
		_dictionary.Clear();
	}


	/// <summary>
	///     Returns an enumerable collection of the items in the set.
	/// </summary>
	/// <returns>
	///     An <see cref="IEnumerable{T}" /> containing all the items in the set.
	/// </returns>
	public IEnumerable<T> AsEnumerable()
	{
		return _dictionary.Keys;
	}
}
