using System.Text.Json;


namespace RakutenDrive.Utils;

/// <summary>
///     Utility class providing helper methods for working with JSON.
/// </summary>
public class JSONUtil
{
	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------
	
	/// <summary>
	///     Attempts to find a string value associated with the specified field name in the provided JSON element.
	/// </summary>
	/// <param name="element">The JSON element to search within.</param>
	/// <param name="field">The field name to search for.</param>
	/// <param name="value">
	///     When this method returns, contains the string value associated with the specified field,
	///     if the field is found and its value is a string; otherwise, null. This parameter is passed uninitialized.
	/// </param>
	/// <returns>
	///     true if the field name is found and its value is a string; otherwise, false.
	/// </returns>
	public static bool TryFindString(JsonElement element, string field, out string? value)
	{
		var fieldLowerCased = field.ToLower();
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in element.EnumerateObject())
			{
				if (property.NameEquals(fieldLowerCased) && property.Value.ValueKind == JsonValueKind.String)
				{
					value = property.Value.GetString()?.ToLower();
					return true;
				}

				if (TryFindString(property.Value, fieldLowerCased, out value))
				{
					return true;
				}
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in element.EnumerateArray())
			{
				if (TryFindString(item, fieldLowerCased, out value))
				{
					return true;
				}
			}
		}

		value = null;
		return false;
	}


	/// <summary>
	///     Converts a JSON string into a pretty-printed, indented format.
	/// </summary>
	/// <param name="json">The JSON string to be pretty-printed.</param>
	/// <returns>
	///     The formatted JSON string with indentation and structure.
	///     If the input string is not a valid JSON, the original string is returned.
	/// </returns>
	public static string Pretty(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (JsonException ex)
		{
			Log.Error($"Can't pretty-print, Invalid JSON: {ex.Message}");
			return json;
		}
	}
}
