using System.Collections.Generic;
using Newtonsoft.Json;


namespace RakutenDrive.Models;

/// <summary>
///     The ActivityLog class provides a set of data structures and models
///     for representing user activity logs in the application.
/// </summary>
internal class ActivityLog
{
	/// <summary>
	///     The TemplateActivityLogVars class defines the structure for containing
	///     specific template-related activity log metadata such as name and size.
	///     It serves as a nested model within the activity logging system for detailed
	///     variable information.
	/// </summary>
	public class TemplateActivityLogVars
	{
		[JsonProperty("name")]
		public string? Name { get; set; }

		[JsonProperty("size")]
		public string? Size { get; set; }
	}


	/// <summary>
	///     The TemplateActivityLog class represents a model for tracking specific template-based
	///     user activity logs within the application. It is used to log actions, categories,
	///     and metadata related to user activities.
	/// </summary>
	public class TemplateActivityLog
	{
		[JsonProperty("user_id")]
		public string? UserID { get; set; }

		[JsonProperty("category")]
		public string? Category { get; set; }

		[JsonProperty("action")]
		public string? Action { get; set; }

		[JsonProperty("title")]
		public string? Title { get; set; }

		[JsonProperty("message")]
		public string? Message { get; set; }

		[JsonProperty("vars")]
		public TemplateActivityLogVars? Vars { get; set; }
	}


	/// <summary>
	///     The TemplatesActivityLogResponse class represents a response model
	///     for activity logs associated with templates. It encapsulates
	///     a collection of template activity log entries that detail specific
	///     actions performed within the application related to templates.
	/// </summary>
	public class TemplatesActivityLogResponse
	{
		[JsonProperty("templates")]
		public List<TemplateActivityLog>? Templates { get; set; }
	}
}
