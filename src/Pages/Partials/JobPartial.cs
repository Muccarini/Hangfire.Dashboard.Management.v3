using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Hangfire.Annotations;
using Hangfire.Common;
using Hangfire.Dashboard.Management.v3.Metadata;
using Hangfire.Server;
using Hangfire.Storage.Monitoring;
using Newtonsoft.Json;
using Hangfire.Dashboard.Management.v3.Support;

namespace Hangfire.Dashboard.Management.v3.Pages.Partials
{
	internal class JobPartial : RazorPage
	{
		public IEnumerable<Func<RazorPage, MenuItem>> Items { get; }
		public readonly string JobId;
		public readonly JobMetadata Job;
		public readonly HashSet<Type> NestedTypes = new HashSet<Type>();

		public JobPartial(string id, JobMetadata job)
		{
			if (id == null) throw new ArgumentNullException(nameof(id));
			if (job == null) throw new ArgumentNullException(nameof(job));
			JobId = id;
			Job = job;
		}

		public override void Execute()
		{
			var inputs = string.Empty;
			int outerDepth = 0;

			foreach (var parameterInfo in Job.MethodInfo.GetParameters()
			.Where(par => Attribute.IsDefined(par, typeof(DisplayDataAttribute))))
			{
				var input = string.Empty;

				if (parameterInfo.ParameterType == typeof(PerformContext)) { continue; }
				if (parameterInfo.ParameterType == typeof(IJobCancellationToken)) { continue; }

				DisplayDataAttribute displayInfo = parameterInfo.GetCustomAttribute<DisplayDataAttribute>();

				bool isList = false;
				Type rootType = parameterInfo.ParameterType;
				List<Type> innerTypes = new List<Type>();

				var labelText = displayInfo?.Label ?? parameterInfo.Name;
				var placeholderText = displayInfo?.Placeholder ?? parameterInfo.Name;
				var myId = $"{JobId}_{parameterInfo.Name}";

				//iterate until (List<List<...<Concrete>> we found a Concrete Type, then processit, at the end we will wrap n-depth times in a panel List
				while (rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(List<>))
				{
					outerDepth++;
					var elementType = rootType.GetGenericArguments().ToList().FirstOrDefault();
					isList = true;
					myId += "_list_0";
					rootType = elementType;
					labelText = rootType.Name + " (Element)";
				}

				if(rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(Nullable<>))
				{
					var elementType = rootType.GetGenericArguments().ToList().FirstOrDefault();
					rootType = elementType;
					labelText = isList ? rootType.Name + " (Element)" : labelText;
				}

				if (rootType.IsInterface)
				{
					if (!VT.Implementations.ContainsKey(rootType)) { inputs += $"<span>No concrete implementation of \"{rootType.Name}\" found in the current assembly.</span>"; continue; }

					var impls = VT.Implementations[rootType];

					if (impls == null || impls.Count == 0)
					{
						input += $"<span>No concrete implementation of \"{rootType.Name}\" found in the current assembly.</span>";
						continue;
					}

					if (impls.Count == 1)
					{
						var implType = impls.First();
						NestedTypes.Add(implType);
						input += $"<div class=\"panel panel-default\"><div class=\"panel-heading\" role=\"button\" data-toggle=\"collapse\" href=\"#collapse_{myId}_{implType.Name}\" aria-expanded=\"false\" 	aria-controls=\"collapse_{myId}_{implType.Name}\"><h4 class=\"panel-title\">{implType.Name} {parameterInfo.Name}</h4></div><div id=\"collapse_{myId}_{implType.Name}\" class=\"panel-collapse collapse\"><div 	class=\"panel-body\">";
						input += InputProps($"{myId}_{implType.Name}", implType, outerDepth);
						input += "</div></div></div>";
						NestedTypes.Remove(implType);
					}
					else
					{
						Type defaultValue = displayInfo.DefaultValue as Type;

						if (defaultValue != null && !rootType.IsAssignableFrom(defaultValue))
						{
							input += $"<span>Default type \"{defaultValue.Name}\" does not implement interface \"{rootType.Name}\".</span>";
							continue;
						}

						//drop down menu for multiple implementations
						input += InputImplsList(myId, displayInfo.CssClasses, labelText, placeholderText, displayInfo.Description, impls, defaultValue, displayInfo.IsDisabled, displayInfo.IsRequired);

						//not showing implementations
						foreach (Type impl in impls)
						{
							NestedTypes.Add(impl);
							string dNone = defaultValue != null && impl == defaultValue ? "" : "d-none";

							input += $"<div id=\"{myId}_{impl.Name}\" class=\"panel panel-default impl-panels-for-{myId} {dNone}\"><div class=\"panel-heading\" role=\"button\" data-toggle=\"collapse\" href=\"#collapse_{myId}_{impl.Name}\" aria-expanded=\"false\" 	aria-controls=\"collapse_{myId}_{impl.Name}\"><h4 class=\"panel-title\">{impl.Name} {parameterInfo.Name}</h4></div><div id=\"collapse_{myId}_{impl.Name}\" 	class=\"panel-collapse collapse\"><div 	class=\"panel-body\">";
							input += InputProps($"{myId}_{impl.Name}", impl, outerDepth);
							input += "</div></div></div>";
							NestedTypes.Remove(impl);
						}
					}
				}
				else if (rootType == typeof(string))
				{
					input += InputTextbox(myId, displayInfo.CssClasses, labelText, placeholderText, displayInfo.Description, displayInfo.DefaultValue, displayInfo.IsDisabled, displayInfo.IsRequired, displayInfo.IsMultiLine);
				}
				else if (rootType == typeof(int) || rootType == typeof(int?))
				{
					input += InputNumberbox(myId, displayInfo.CssClasses, labelText, placeholderText, displayInfo.Description, displayInfo.DefaultValue, displayInfo.IsDisabled, displayInfo.IsRequired);
				}
				else if (rootType == typeof(Uri))
				{
					input += Input(myId, displayInfo.CssClasses, labelText, placeholderText, displayInfo.Description, "url", displayInfo.DefaultValue, displayInfo.IsDisabled, displayInfo.IsRequired);
				}
				else if (rootType == typeof(DateTime) || rootType == typeof(DateTime?))
				{
					input += InputDatebox(myId, displayInfo.CssClasses, labelText, placeholderText, displayInfo.Description, displayInfo.DefaultValue, displayInfo.IsDisabled, displayInfo.IsRequired, displayInfo.ControlConfiguration);
				}
				else if (rootType == typeof(bool))
				{
					input += "<br/>" + InputCheckbox(myId, displayInfo.CssClasses, labelText, placeholderText, displayInfo.Description, displayInfo.DefaultValue, displayInfo.IsDisabled);
				}
				else if (rootType.IsEnum || (rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(Nullable<>) && rootType.GetGenericArguments()[0].IsEnum))
				{
					var data = new Dictionary<string, int>();
					foreach (var name in Enum.GetNames(rootType))
					{
						var value = (int)Enum.Parse(rootType, name);
						data.Add(name, value);
					}

					input += InputDataList(myId, displayInfo.CssClasses, labelText, placeholderText, displayInfo.Description, data, displayInfo.DefaultValue?.ToString(), displayInfo.IsDisabled);
				}
				else if (rootType.IsClass)
				{
					NestedTypes.Add(rootType);
					input += $"<div class=\"panel panel-default\"><div class=\"panel-heading\" role=\"button\" data-toggle=\"collapse\" href=\"#collapse_{myId}\" aria-expanded=\"false\" aria-controls=\"collapse_{myId}\"><h4 class=\"panel-title\">{labelText}</h4></div><div id=\"collapse_{myId}\" class=\"panel-collapse collapse\"><div class=\"panel-body\">";
					input += InputProps(myId, rootType, outerDepth);
					input += "</div></div></div>";
					NestedTypes.Remove(rootType);
				}
				else
				{
					input += $"<span> Unsupported type: {rootType.Name} </span>";
					//input += InputTextbox(myId, displayInfo.CssClasses, labelText, placeholderText, displayInfo.Description, displayInfo.DefaultValue, displayInfo.IsDisabled, displayInfo.IsRequired);
				}

				//wrapper
				if (isList)
				{
					int depthCouter = outerDepth;
					while (depthCouter > 0)
					{
						depthCouter--;
						labelText = displayInfo.Label == null ? parameterInfo.Name : displayInfo.Label;
						labelText += ": ";
						for (int j = 0; j < (outerDepth - depthCouter); j++)
						{
							labelText += " Collection of ";
						}
						labelText += $" {rootType.Name}";

						if (myId.EndsWith("_list_0"))
						{
							myId = myId.Substring(0, myId.Length - "_list_0".Length);
						}

						input = InputList(myId, input, depthCouter, labelText);
					}
					outerDepth = 0;
				}

				inputs += input;
			}

			if (string.IsNullOrWhiteSpace(inputs))
			{
				inputs = "<span>This job does not require inputs</span>";
			}

			WriteLiteral($@"
				<div class=""well"">
					{inputs}
				</div>
				<div id=""{JobId}_error""></div>
				<div id=""{JobId}_success""></div>
			");
		}

		protected string InputProps(string parentId, Type parentType, int outerDepth)
		{
			int innerDepth = outerDepth;
			string inputs = string.Empty;

			foreach (var propertyInfo in parentType.GetProperties()
				.Where(prop => Attribute.IsDefined(prop, typeof(DisplayDataAttribute))))
			{
				var input = string.Empty;
				var myId = $"{parentId}_{propertyInfo.Name}";
				var propDisplayInfo = propertyInfo.GetCustomAttribute<DisplayDataAttribute>() ?? new DisplayDataAttribute();
				var labelText = propDisplayInfo.Label ?? propertyInfo.Name;

				bool isList = false;
				Type rootType = propertyInfo.PropertyType;
				List<Type> innerTypes = new List<Type>();

				//iterate until (List<List<...<Concrete>> we found a Concrete Type, then processit, at the end we will wrap n-depth times in a panel List
				while (rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(List<>))
				{
					innerDepth++;
					var elementType = rootType.GetGenericArguments().ToList().FirstOrDefault();
					isList = true;
					myId += "_list_0";
					rootType = elementType;
					labelText = rootType.Name + " (Element)";
				}

				if (rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(Nullable<>))
				{
					var elementType = rootType.GetGenericArguments().ToList().FirstOrDefault();
					rootType = elementType;
					labelText = isList ? rootType.Name + " (Element)" : labelText;
				}

				if (rootType.IsInterface)
				{
					if (!VT.Implementations.ContainsKey(rootType)) { input += $"<span>No concrete implementation of \"{rootType}\" found in the current assembly.</span>"; continue; }

					var impls = VT.Implementations[rootType];

					if (impls == null || impls.Count == 0)
					{
						input += $"<span>No concrete implementation of \"{rootType.Name}\" found in the current assembly.</span>";
						continue;
					}

					if (impls.Count == 1)
					{
						var implType = impls.First();
						if (!NestedTypes.Add(implType)) { input += "<span>Circular reference detected, not allowed.</span>"; continue; }
						input += $"<div class=\"panel panel-default\"><div class=\"panel-heading\" role=\"button\" data-toggle=\"collapse\" href=\"#collapse_{myId}_{implType.Name}\" aria-expanded=\"false\" 	aria-controls=\"collapse_{myId}_{implType.Name}\"><h4 class=\"panel-title\">{implType.Name}</h4></div><div id=\"collapse_{myId}_{implType.Name}\" class=\"panel-collapse collapse\"><div class=\"panel-body\">";
						input += InputProps($"{myId}_{implType.Name}", implType, innerDepth);
						input += "</div></div></div>";
						NestedTypes.Remove(implType);
					}
					else
					{
						var filteredImpls = new HashSet<Type>(impls.Where(impl => !NestedTypes.Contains(impl)));

						Type defaultValue = propDisplayInfo.DefaultValue as Type;

						if (defaultValue != null)
						{
							if (!rootType.IsAssignableFrom(defaultValue))
							{
								input += $"<span>Default type \"{defaultValue.Name}\" does not implement interface \"{rootType.Name}\".</span>";
								
								continue;
							}
							if (!impls.Contains(defaultValue))
							{
								input += $"<span>Default type \"{defaultValue.Name}\" is not in the list of implementations.</span>";
								continue;
							}
							if (!filteredImpls.Contains(defaultValue))
							{
								input += $"<span>Default type \"{defaultValue.Name}\" creates a circular reference and is not allowed.</span>";
								continue;
							}
						}

						//drop down menu
						input += InputImplsList(myId, propDisplayInfo.CssClasses, labelText, propDisplayInfo.Placeholder, propDisplayInfo.Description, filteredImpls, defaultValue, propDisplayInfo.IsDisabled, propDisplayInfo.IsRequired);

						//implementations
						foreach (Type impl in impls)
						{
							if (!NestedTypes.Add(impl)) { continue; }
							string dNone = defaultValue != null && impl == defaultValue ? "" : "d-none";
							input += $"<div id=\"{myId}_{impl.Name}\" class=\"panel panel-default impl-panels-for-{myId} {dNone}\"><div class=\"panel-heading\" role=\"button\" data-toggle=\"collapse\" href=\"#collapse_{myId}_{impl.Name}\" aria-expanded=\"false\" 	aria-controls=\"collapse_{myId}_{impl.Name}\"><h4 class=\"panel-title\">{impl.Name} {propertyInfo.Name}</h4></div><div id=\"collapse_{myId}_{impl.Name}\" 	class=\"panel-collapse collapse\"><div 	class=\"panel-body\">";
							input += InputProps($"{myId}_{impl.Name}", impl, innerDepth);
							input += "</div></div></div>";
							NestedTypes.Remove(impl);
						}
					}
				}
				else if (rootType == typeof(string))
				{
					input += InputTextbox(myId, propDisplayInfo.CssClasses, labelText, propDisplayInfo.Placeholder, propDisplayInfo.Description, propDisplayInfo.DefaultValue, propDisplayInfo.IsDisabled, propDisplayInfo.IsRequired, propDisplayInfo.IsMultiLine);
				}
				else if (rootType == typeof(int))
				{
					input += InputNumberbox(myId, propDisplayInfo.CssClasses, labelText, propDisplayInfo.Placeholder, propDisplayInfo.Description, propDisplayInfo.DefaultValue, propDisplayInfo.IsDisabled, propDisplayInfo.IsRequired);
				}
				else if (rootType == typeof(Uri))
				{
					input += Input(myId, propDisplayInfo.CssClasses, labelText, propDisplayInfo.Placeholder, propDisplayInfo.Description, "url", propDisplayInfo.DefaultValue, propDisplayInfo.IsDisabled, propDisplayInfo.IsRequired);
				}
				else if (rootType == typeof(DateTime))
				{
					input += InputDatebox(myId, propDisplayInfo.CssClasses, labelText, propDisplayInfo.Placeholder, propDisplayInfo.Description, propDisplayInfo.DefaultValue, propDisplayInfo.IsDisabled, propDisplayInfo.IsRequired, propDisplayInfo.ControlConfiguration);
				}
				else if (rootType == typeof(bool))
				{
					input += "<br/>" + InputCheckbox(myId, propDisplayInfo.CssClasses, labelText, propDisplayInfo.Placeholder, propDisplayInfo.Description, propDisplayInfo.DefaultValue, propDisplayInfo.IsDisabled);
				}
				else if (rootType.IsEnum)
				{
					var data = new Dictionary<string, int>();
					foreach (var name in Enum.GetNames(rootType))
					{
						var value = (int)Enum.Parse(rootType, name);
						data.Add(name, value);
					}

					input += InputDataList(myId, propDisplayInfo.CssClasses, labelText, propDisplayInfo.Placeholder, propDisplayInfo.Description, data, propDisplayInfo.DefaultValue?.ToString(), propDisplayInfo.IsDisabled);
				}
				else if (rootType.IsClass)
				{
					if (!NestedTypes.Add(rootType)) { input += "<span>Circular reference detected, not allowed.</span>"; continue; } //Circular reference, not allowed -> null
					input += $"<div class=\"panel panel-default\"><div class=\"panel-heading\" role=\"button\" data-toggle=\"collapse\" href=\"#collapse_{myId}\" aria-expanded=\"false\" aria-controls=\"collapse_{myId}\"><h4 class=\"panel-title\">{labelText}</h4></div><div id=\"collapse_{myId}\" class=\"panel-collapse collapse\"><div class=\"panel-body\">";
					input += InputProps(myId, rootType, innerDepth);
					input += "</div></div></div>";
					NestedTypes.Remove(rootType);
				}
				else
				{
					input += InputTextbox(myId, propDisplayInfo.CssClasses, labelText, propDisplayInfo.Placeholder, propDisplayInfo.Description, propDisplayInfo.DefaultValue, propDisplayInfo.IsDisabled, propDisplayInfo.IsRequired);
				}
				
				//wrapper
				if (isList)
				{
					int depthCouter = innerDepth;
					while (depthCouter > outerDepth)
					{
						depthCouter--;
						labelText = propDisplayInfo.Label == null ? propertyInfo.Name : propDisplayInfo.Label;
						labelText += ": ";
						for (int j = 0; j < (innerDepth - depthCouter); j++)
						{
							labelText += " Collection of ";
						}
						labelText += $" {rootType.Name}";

						if (myId.EndsWith("_list_0"))
						{
							myId = myId.Substring(0, myId.Length - "_list_0".Length);
						}

						input = InputList(myId, input, depthCouter, labelText);
					}
					innerDepth = outerDepth; //reset depth on the parent level
				}

				inputs += input;
			}

			if (string.IsNullOrWhiteSpace(inputs))
			{
				inputs = $"<span>No valid <strong>public</strong> properties with the <strong>[DisplayData]</strong> attribute were found in <strong>{parentType.Name}</strong>.</span>";
			}

			return inputs;
		}

		protected string InputList(string myId, string nestedInput, int depth, string labelText)
		{
					return $@"
					<div class=""panel panel-default"">
						<div class=""panel-heading"" role=""button"" data-toggle=""collapse"" href=""#collapse_{myId}"" aria-expanded=""false"" aria-controls=""collapse_{myId}"">
							<h4 class=""panel-title"">{labelText}</h4>
						</div>
						<div id=""collapse_{myId}"" class=""panel-collapse collapse"">
							<div id=""{myId}"" class=""panel-body list-element-container"" data-list-length=""0"">
								<!-- ELEMENT -->
								<div data-index=0 data-depth={depth} class=""d-none"">
									<div class=""content col-xs-11"">
										<!-- CONTENT -->
										{nestedInput}
									</div>
									<div class=""col-xs-1 pr-0"">
										<button type=""button"" class=""element-deleter btn btn-sm"">
											<i class=""fas fa-trash""></i>
										</button>
									</div>
								</div>
								<button type=""button"" class=""btn btn-sm element-adder"">
    								<i class=""fas fa-plus""></i>
								</button>
							</div>
						</div>
					</div>
					";
		}

		protected string Input(string id, string cssClasses, string labelText, string placeholderText, string descriptionText, string inputtype, object defaultValue = null, bool isDisabled = false, bool isRequired = false)
		{
			var control = $@"
<div class=""form-group {cssClasses} {(isRequired ? "required" : "")}"">
	<label for=""{id}"" class=""control-label"">{labelText}</label>
";

			if (inputtype == "textarea")
			{
				control += $@"
	<textarea rows=""10"" class=""hdm-job-input hdm-input-textarea form-control"" placeholder=""{placeholderText}"" id=""{id}"" {(isDisabled ? "disabled='disabled'" : "")} {(isRequired ? "required='required'" : "")}>{defaultValue}</textarea>
";
			}
			else
			{
				control += $@"
	<input class=""hdm-job-input hdm-input-{inputtype} form-control"" type=""{inputtype}"" placeholder=""{placeholderText}"" id=""{id}"" value=""{defaultValue}"" {(isDisabled ? "disabled='disabled'" : "")} {(isRequired ? "required='required'" : "")} />
";
			}

			if (!string.IsNullOrWhiteSpace(descriptionText))
			{
				control += $@"
	<small id=""{id}Help"" class=""form-text text-muted"">{descriptionText}</small>
";
			}
			control += $@"
</div>";
			return control;
		}

		protected string InputTextbox(string id, string cssClasses, string labelText, string placeholderText, string descriptionText, object defaultValue = null, bool isDisabled = false, bool isRequired = false, bool isMultiline = false)
		{
			return Input(id, cssClasses, labelText, placeholderText, descriptionText, isMultiline ? "textarea" : "text", defaultValue, isDisabled, isRequired);
		}

		protected string InputNumberbox(string id, string cssClasses, string labelText, string placeholderText, string descriptionText, object defaultValue = null, bool isDisabled = false, bool isRequired = false)
		{
			return Input(id, cssClasses, labelText, placeholderText, descriptionText, "text", defaultValue, isDisabled, isRequired);
		}

		protected string InputDatebox(string id, string cssClasses, string labelText, string placeholderText, string descriptionText, object defaultValue = null, bool isDisabled = false, bool isRequired = false, string controlConfig = "")
		{
			if (!string.IsNullOrWhiteSpace(controlConfig))
			{
				controlConfig = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(controlConfig), Formatting.None);
			}
			return $@"
<div class=""form-group {cssClasses} {(isRequired ? "required" : "")}"">
	<label for=""{id}"" class=""control-label"">{labelText}</label>
	<div class='hdm-job-input-container hdm-input-date-container input-group date' id='{id}_datetimepicker' data-td_options='{controlConfig}' data-td_value='{defaultValue}'>
		<input type='text' class=""hdm-job-input hdm-input-date form-control"" placeholder=""{placeholderText}"" {(isDisabled ? "disabled='disabled'" : "")} {(isRequired ? "required='required'" : "")} />
		<span class=""input-group-addon"">
			<span class=""glyphicon glyphicon-calendar""></span>
		</span>
	</div>
		{(!string.IsNullOrWhiteSpace(descriptionText) ? $@"
		<small id=""{id}Help"" class=""form-text text-muted"">{descriptionText}</small>
" : "")}
</div>";
		}

		protected string InputCheckbox(string id, string cssClasses, string labelText, string placeholderText, string descriptionText, object defaultValue = null, bool isDisabled = false)
		{
			var bDefaultValue = (bool)(defaultValue ?? false);

			return $@"
<div class=""form-group {cssClasses}"">
	<div class=""form-check"">
		<input class=""hdm-job-input hdm-input-checkbox form-check-input"" type=""checkbox"" id=""{id}"" {(bDefaultValue ? "checked='checked'" : "")} {(isDisabled ? "disabled='disabled'" : "")} />
		<label class=""form-check-label"" for=""{id}"">{labelText}</label>
	</div>
		{(!string.IsNullOrWhiteSpace(descriptionText) ? $@"
		<small id=""{id}Help"" class=""form-text text-muted"">{descriptionText}</small>
" : "")}
</div>";
		}

		protected string InputDataList(string id, string cssClasses, string labelText, string placeholderText, string descriptionText, Dictionary<string, int> data, string defaultValue = null, bool isDisabled = false)
		{
			var initText = defaultValue != null ? defaultValue : !string.IsNullOrWhiteSpace(placeholderText) ? placeholderText : "Select a value";
			var initValue = defaultValue != null && data.ContainsKey(defaultValue) ? data[defaultValue].ToString() : "";
			var output = $@"
<div class=""form-group {cssClasses}"">
	<label class=""control-label"">{labelText}</label>
	<div class=""dropdown"">
		<button id=""{id}"" class=""hdm-job-input hdm-input-datalist btn btn-default dropdown-toggle input-control-data-list"" type=""button"" data-selectedvalue=""{initValue}"" data-toggle=""dropdown"" aria-haspopup=""true"" aria-expanded=""false"" {(isDisabled ? "disabled='disabled'" : "")}>
			<span class=""{id} input-data-list-text pull-left"">{initText}</span>
			<span class=""caret""></span>
		</button>
		<ul class=""dropdown-menu data-list-options"" data-optionsid=""{id}"" aria-labelledby=""{id}"">";
			foreach (var item in data)
			{
				output += $@"
			<li><a href=""javascript:void(0)"" class=""option"" data-optiontext=""{item.Key}"" data-optionvalue=""{item.Value}"">{item.Key}</a></li>
";
			}

			output += $@"
		</ul>
	</div>
	{(!string.IsNullOrWhiteSpace(descriptionText) ? $@"
		<small id=""{id}Help"" class=""form-text text-muted"">{descriptionText}</small>
" : "")}
</div>";

			return output;
		}
		
        protected string InputImplsList(string id, string cssClasses, string labelText, string placeholderText, string descriptionText, HashSet<Type> impls, Type defaultValue = null, bool isDisabled = false, bool isRequired = false)
        {
			var initText = placeholderText ?? "Select your own implementation";
			var initValue = initText;

			if (defaultValue != null && impls.Contains(defaultValue))
			{
				var defaultType = impls.FirstOrDefault(impl => impl == defaultValue);
				initValue = defaultType.FullName;
				initText = defaultType.Name;
			}

            var output = $@"
            <div class=""form-group {cssClasses} {(isRequired ? "required" : "")}"">
                <label class=""control-label"">{labelText}</label>
                <div class=""dropdown"">
                    <button id=""{id}"" class=""hdm-impl-selector-button hdm-job-input hdm-input-datalist btn btn-default dropdown-toggle input-control-data-list"" type=""button"" data-selectedvalue=""{initValue}"" data-toggle=""dropdown"" aria-haspopup=""true"" aria-expanded=""false"" {(isDisabled ? "disabled='disabled'" : "")}>
                        <span class=""{id} input-data-list-text pull-left"">{initText}</span>
                        <span class=""caret""></span>
                    </button>
                    <ul class=""dropdown-menu data-list-options impl-selector-options"" data-optionsid=""{id}"" aria-labelledby=""{id}"">";
                        foreach (var impl in impls)
                        {
                            var targetPanelId = $"{id}_{impl.Name}";
                            output += $@"
                        <li><a class=""option"" data-optiontext=""{impl.Name}"" data-optionvalue=""{impl.FullName}"" data-target-panel-id=""{targetPanelId}"">{impl.Name}</a></li>";
                        }
            
                        output += $@"
                    </ul>
                </div>
                {(!string.IsNullOrWhiteSpace(descriptionText) ? $@"
                    <small id=""{id}Help"" class=""form-text text-muted"">{descriptionText}</small>
            " : "")}
            </div>";

            return output;
        }
	}
}
