using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Hangfire.Common;
using Hangfire.Dashboard;
using Hangfire.Dashboard.Management.v3.Metadata;
using Hangfire.Dashboard.Management.v3.Support;
using Hangfire.Dashboard.Pages;
using Hangfire.Server;
using Hangfire.States;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hangfire.Dashboard.Management.v3.Pages
{
	partial class ManagementBasePage
	{
		public readonly string menuName;
		public readonly IEnumerable<JobMetadata> jobs;
		public readonly Dictionary<string, string> jobSections;

		protected internal ManagementBasePage(string menuName) : base()
		{
			//this.UrlHelper = new UrlHelper(this.Context);
			this.menuName = menuName;

			jobs = JobsHelper.Metadata.Where(j => j.MenuName.Contains(menuName)).OrderBy(x => x.SectionTitle).ThenBy(x => x.Name);
			jobSections = jobs.Select(j => j.SectionTitle).Distinct().ToDictionary(k => k, v => string.Empty);
		}

		public static void AddCommands(string menuName)
		{
			var jobs = JobsHelper.Metadata.Where(j => j.MenuName.Contains(menuName));

			foreach (var jobMetadata in jobs)
			{
				var route = $"{ManagementPage.UrlRoute}/{jobMetadata.JobId.ScrubURL()}";

				DashboardRoutes.Routes.Add(route, new CommandWithResponseDispatcher(context => {
					string errorMessage = null;
					string jobLink = null;
					var par = new List<object>();
					string GetFormVariable(string key)
					{
						return Task.Run(() => context.Request.GetFormValuesAsync(key)).Result.FirstOrDefault();
					}

					var id = GetFormVariable("id");
					var type = GetFormVariable("type");

					HashSet<Type> nestedTypes = new HashSet<Type>();

					foreach (var parameterInfo in jobMetadata.MethodInfo.GetParameters())
					{
						if (parameterInfo.ParameterType == typeof(PerformContext) || parameterInfo.ParameterType == typeof(IJobCancellationToken))
						{
							par.Add(null);
							continue;
						}

						DisplayDataAttribute displayInfo = null;

						if (parameterInfo.GetCustomAttributes(true).OfType<DisplayDataAttribute>().Any())
						{
							displayInfo = parameterInfo.GetCustomAttribute<DisplayDataAttribute>(true);
						}
						else
						{
							displayInfo = new DisplayDataAttribute();
						}

						Type rootType = parameterInfo.ParameterType;

						var variable = $"{id}_{parameterInfo.Name}";

						if (rootType == typeof(DateTime) || rootType == typeof(DateTime?))
						{
							variable = $"{variable}_datetimepicker";
						}

						variable = variable.Trim('_');
						var formInput = GetFormVariable(variable);

						object item = null;

						if (rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(List<>))
						{
							var elementType = rootType.GetGenericArguments()[0];
							var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));

							if (int.TryParse(GetFormVariable($"{variable}"), out int count))
							{
								for (int i = 0; i < count; i++)
								{
									nestedTypes.Add(elementType);
									var nestedInstance = ProcessType($"{variable}_list_{i}", elementType, GetFormVariable, nestedTypes, out errorMessage);
									nestedTypes.Remove(elementType);
									list.GetType().GetMethod("Add").Invoke(list, new[] { nestedInstance });
								}
							}

							item = list;

						}
						else if (rootType.IsInterface)
						{
							if (!VT.Implementations.ContainsKey(rootType)) { errorMessage = $"{displayInfo.Label ?? parameterInfo.Name}: No concrete implementation of \"{rootType.Name}\" found in the current assembly."; break; }
							var impls = VT.Implementations[rootType];

							if (impls == null || impls.Count == 0)
							{
								errorMessage = $"No concrete implementation of \"{rootType.Name}\" found in the current assembly.";
								break;
							}

							var impl = impls.FirstOrDefault(concrete => concrete.FullName == GetFormVariable($"{id}_{parameterInfo.Name}"));

							if (impl == null)
							{
								if (displayInfo.IsRequired)
								{
									errorMessage = $"{displayInfo.Label ?? parameterInfo.Name} is required.";
									break;
								}
								errorMessage = $"Selected implementation: \"" + GetFormVariable($"{id}_{parameterInfo.Name}") + $"\"  for {displayInfo.Label ?? parameterInfo.Name} not found in VT";
								break;
							}

							nestedTypes.Add(impl);
							item = ProcessType($"{variable}_{impl.Name}", impl, GetFormVariable, nestedTypes, out errorMessage);
							nestedTypes.Remove(impl);
						}
						else if (rootType == typeof(string))
						{
							item = formInput;
							if (displayInfo.IsRequired && string.IsNullOrWhiteSpace((string)item))
							{
								errorMessage = $"{displayInfo.Label ?? parameterInfo.Name} is required.";
								break;
							}
						}
						else if (rootType == typeof(int) || rootType == typeof(int?))
						{
							int intNumber = 0;
							item = intNumber;
							if (int.TryParse(formInput, out intNumber) == true)
							{
								item = intNumber;
							}
							else
							{
								if (formInput != "")
								{
									errorMessage = $"{displayInfo.Label ?? parameterInfo.Name} incorrect format";
									break;
								}
								if (displayInfo.IsRequired)
								{
									errorMessage = $"{displayInfo.Label ?? parameterInfo.Name} is required.";
									break;
								}
								if (rootType == typeof(int?))
								{
									item = null;
								}
							}
						}
						else if (rootType == typeof(DateTime) || rootType == typeof(DateTime?))
						{
							item = formInput == null ? DateTime.MinValue : DateTime.Parse(formInput, null, DateTimeStyles.RoundtripKind);
							if (item.Equals(DateTime.MinValue))
							{
								if (displayInfo.IsRequired)
								{
									errorMessage = $"{displayInfo.Label ?? parameterInfo.Name} is required.";
									break;
								}
								if (rootType == typeof(DateTime?))
								{
									item = null;
								}
							}
						}
						else if (rootType == typeof(bool))
						{
							item = formInput == "on";
						}
						else if (rootType.IsEnum || (rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(Nullable<>) && rootType.GetGenericArguments()[0].IsEnum))
						{							
							Type el = null;

							try
							{
								el = rootType;

								if ((rootType.IsGenericType && el.GetGenericTypeDefinition() == typeof(Nullable<>) && rootType.GetGenericArguments()[0].IsEnum))
								{
									el = rootType.GetGenericArguments()[0];
								}

								item = Enum.Parse(el, formInput);
							}
							catch
							{
								item = (rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(Nullable<>) && rootType.GetGenericArguments()[0].IsEnum) ?
									null : GetDefaultEnumValue(el);
							}
						}
						else if (rootType.IsClass || rootType.IsClass || (rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(Nullable<>) && rootType.GetGenericArguments()[0].IsClass))
						{
							Type typeToProcess = rootType;
							if ((rootType.IsGenericType && rootType.GetGenericTypeDefinition() == typeof(Nullable<>) && rootType.GetGenericArguments()[0].IsClass))
							{
								typeToProcess = rootType.GetGenericArguments()[0];
							}

							nestedTypes.Add(typeToProcess);
							item = ProcessType(variable, typeToProcess, GetFormVariable, nestedTypes, out errorMessage);
							nestedTypes.Remove(typeToProcess);
						}
						else if (!rootType.IsValueType)
						{
							if (formInput == null || formInput.Length == 0)
							{
								item = null;
								if (displayInfo.IsRequired)
								{
									errorMessage = $"{displayInfo.Label ?? parameterInfo.Name} is required.";
									break;
								}
							}
							else
							{
								item = JsonConvert.DeserializeObject(formInput, rootType);
							}
						}
						else
						{
							item = formInput;
						}

						par.Add(item);
					}

					if (errorMessage == null)
					{
						var array = par.ToArray();
						var job = new Job(jobMetadata.Type, jobMetadata.MethodInfo, par.ToArray());
						var client = new BackgroundJobClient(context.Storage);
						switch (type)
						{
							case "CronExpression":
								{
									var manager = new RecurringJobManager(context.Storage);
									var cron = GetFormVariable($"{id}_sys_cron");
									var name = GetFormVariable($"{id}_sys_name");

									if (string.IsNullOrWhiteSpace(cron))
									{
										errorMessage = "No Cron Expression Defined";
										break;
									}
									if (jobMetadata.AllowMultiple && string.IsNullOrWhiteSpace(name))
									{
										errorMessage = "No Job Name Defined";
										break;
									}

									try
									{
										var jobId = jobMetadata.AllowMultiple ? name : jobMetadata.JobId;
										manager.AddOrUpdate(jobId, job, cron, TimeZoneInfo.Local, jobMetadata.Queue);
										jobLink = new UrlHelper(context).To("/recurring");
									}
									catch (Exception e)
									{
										errorMessage = e.Message;
									}
									break;
								}
							case "ScheduleDateTime":
								{
									var datetime = GetFormVariable($"{id}_sys_datetime");

									if (string.IsNullOrWhiteSpace(datetime))
									{
										errorMessage = "No Schedule Defined";
										break;
									}

									if (!DateTime.TryParse(datetime, null, DateTimeStyles.RoundtripKind, out DateTime dt))
									{
										errorMessage = "Unable to parse Schedule";
										break;
									}
									try
									{
										var jobId = client.Create(job, new ScheduledState(dt.ToUniversalTime()));//Queue
										jobLink = new UrlHelper(context).JobDetails(jobId);
									}
									catch (Exception e)
									{
										errorMessage = e.Message;
									}
									break;
								}
							case "ScheduleTimeSpan":
								{
									var timeSpan = GetFormVariable($"{id}_sys_timespan");

									if (string.IsNullOrWhiteSpace(timeSpan))
									{
										errorMessage = $"No Delay Defined '{id}'";
										break;
									}

									if (!TimeSpan.TryParse(timeSpan, out TimeSpan dt))
									{
										errorMessage = "Unable to parse Delay";
										break;
									}

									try
									{
										var jobId = client.Create(job, new ScheduledState(dt));//Queue
										jobLink = new UrlHelper(context).JobDetails(jobId);
									}
									catch (Exception e)
									{
										errorMessage = e.Message;
									}
									break;
								}
							case "Enqueue":
							default:
								{
									try
									{
										var jobId = client.Create(job, new EnqueuedState(jobMetadata.Queue));
										jobLink = new UrlHelper(context).JobDetails(jobId);
									}
									catch (Exception e)
									{
										errorMessage = e.Message;
									}
									break;
								}
						}
					}

					context.Response.ContentType = "application/json";
					if (!string.IsNullOrEmpty(jobLink))
					{
						context.Response.StatusCode = (int)HttpStatusCode.OK;
						context.Response.WriteAsync(JsonConvert.SerializeObject(new { jobLink }));
						return true;
					}
					context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
					context.Response.WriteAsync(JsonConvert.SerializeObject(new { errorMessage }));

					return false;
				}));
			}
		}

		private static object ProcessType(string parentId, Type parentType, Func<string, string> getFormVariable, HashSet<Type> nestedTypes, out string errorMessage)
		{
			errorMessage = null;

			if (parentType == typeof(DateTime) || parentType == typeof(DateTime?))
			{
				parentId = $"{parentId}_datetimepicker";
			}

			if (parentType == typeof(string))
			{
				return getFormVariable(parentId);	
			}
			else if (parentType == typeof(int) || parentType == typeof(int?))
			{
				int intNumber;
				if (int.TryParse(getFormVariable(parentId), out intNumber) == false)
				{
					if (getFormVariable(parentId) != "")
					{
						errorMessage = $"{parentId} incorrect format";
						return null;
					}
					if (parentType == typeof(int?))
					{
						return null;
					}
				}
				return intNumber;
			}
			else if (parentType == typeof(DateTime) || parentType == typeof(DateTime?))
			{
				var val = getFormVariable(parentId) == null ? DateTime.MinValue : DateTime.Parse(getFormVariable(parentId), null, DateTimeStyles.RoundtripKind);
				if (val.Equals(DateTime.MinValue))
				{
					if (parentType == typeof(DateTime?))
					{
						return null;
					}
				}

				return val;
			}
			else if (parentType.IsEnum || (parentType.IsGenericType && parentType.GetGenericTypeDefinition() == typeof(Nullable<>) && parentType.GetGenericArguments()[0].IsEnum))
			{
				Type el = null;

				try
				{
					el = parentType;

					if ((parentType.IsGenericType && el.GetGenericTypeDefinition() == typeof(Nullable<>) && parentType.GetGenericArguments()[0].IsEnum))
					{
						el = parentType.GetGenericArguments()[0];
					}

					return Enum.Parse(el, getFormVariable(parentId));
				}
				catch
				{
					return (parentType.IsGenericType && parentType.GetGenericTypeDefinition() == typeof(Nullable<>) && parentType.GetGenericArguments()[0].IsEnum) ?
						null : GetDefaultEnumValue(el);
				}
			}
			else if (parentType == typeof(bool))
			{
				return getFormVariable(parentId) == "on";
			}

			if (parentType.IsGenericType && (parentType.GetGenericTypeDefinition() == typeof(List<>)))
			{
				var elementType = parentType.GetGenericArguments()[0];
				var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));

				if (int.TryParse(getFormVariable($"{parentId}"), out int count))
				{
					for (int i = 0; i < count; i++)
					{
						nestedTypes.Add(elementType);
						var nestedInstance = ProcessType($"{parentId}_list_{i}", elementType, getFormVariable, nestedTypes, out errorMessage);
						nestedTypes.Remove(elementType);
						list.GetType().GetMethod("Add").Invoke(list, new[] { nestedInstance });
					}
				}

				return list;
			}

			if (parentType.IsInterface)
			{
				if (!VT.Implementations.ContainsKey(parentType))
				{
					errorMessage = $"{parentType.Name} is not a valid interface type or is not registered in VT.";
					return null;
				}

				VT.Implementations.TryGetValue(parentType, out HashSet<Type> impls);
				var filteredImpls = new HashSet<Type>(impls.Where(impl => !nestedTypes.Contains(impl)));

				var choosedImpl = impls.FirstOrDefault(concrete => concrete.FullName == getFormVariable($"{parentId}"));

				if (choosedImpl == null)
				{
					errorMessage = $"cannot find a valid concrete type of {parentType} or is not registered in VT.";
					return null;
				}

				nestedTypes.Add(choosedImpl);
				var nestedInstance = ProcessType($"{parentId}_{choosedImpl.Name}", choosedImpl, getFormVariable, nestedTypes, out errorMessage);
				nestedTypes.Remove(choosedImpl);

				return nestedInstance;
			}

			if (parentType.IsClass || (parentType.IsGenericType && parentType.GetGenericTypeDefinition() == typeof(Nullable<>) && parentType.GetGenericArguments()[0].IsClass))
			{
				Type typeToProcess = parentType;
				if (parentType.IsGenericType && parentType.GetGenericTypeDefinition() == typeof(Nullable<>) && parentType.GetGenericArguments()[0].IsClass)
				{
					typeToProcess = parentType.GetGenericArguments()[0];
				}

				var instance = Activator.CreateInstance(typeToProcess);
				if (instance == null)
				{
					errorMessage = $"Unable to create instance of {parentType.Name}";
					return null;
				}

				foreach (var propertyInfo in parentType.GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(DisplayDataAttribute))))
				{
					string propId = $"{parentId}_{propertyInfo.Name}";

					if (propertyInfo.PropertyType == typeof(DateTime) || propertyInfo.PropertyType == typeof(DateTime?))
					{
						propId = $"{propId}_datetimepicker";
					}

					var propDisplayInfo = propertyInfo.GetCustomAttribute<DisplayDataAttribute>();
					var propLabel = propDisplayInfo.Label ?? propertyInfo.Name;

					var formInput = getFormVariable(propId);

					if (parentType.IsGenericType && parentType.GetGenericTypeDefinition() == typeof(List<>))
					{
						var elementType = parentType.GetGenericArguments()[0];
						var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));

						if (int.TryParse(getFormVariable($"{propId}"), out int count))
						{
							for (int i = 0; i < count; i++)
							{
								nestedTypes.Add(elementType);
								var nestedInstance = ProcessType($"{propId}_list_{i}", elementType, getFormVariable, nestedTypes, out errorMessage);
								nestedTypes.Remove(elementType);
								list.GetType().GetMethod("Add").Invoke(list, new[] { nestedInstance });
							}
						}

						propertyInfo.SetValue(instance, list);
					}
					else if (propertyInfo.PropertyType.IsInterface)
					{
						if (!VT.Implementations.ContainsKey(propertyInfo.PropertyType)) { errorMessage = $"{propDisplayInfo.Label ?? propertyInfo.Name} is not a valid interface type or is not 	registered in VT."; break; }
						VT.Implementations.TryGetValue(propertyInfo.PropertyType, out HashSet<Type> impls);
						var filteredImpls = new HashSet<Type>(impls.Where(impl => !nestedTypes.Contains(impl)));

						var choosedImpl = impls.FirstOrDefault(concrete => concrete.FullName == getFormVariable($"{propId}"));

						if (choosedImpl == null)
						{
							errorMessage = $"cannot find a valid concrete type of {propertyInfo.PropertyType} or is not registered in VT.";
							break;
						}

						nestedTypes.Add(choosedImpl);
						var nestedInstance = ProcessType($"{propId}_{choosedImpl.Name}", choosedImpl, getFormVariable, nestedTypes, out errorMessage);
						nestedTypes.Remove(choosedImpl);

						propertyInfo.SetValue(instance, nestedInstance);
					}
					else if (propertyInfo.PropertyType == typeof(string))
					{
						propertyInfo.SetValue(instance, formInput);
						if (propDisplayInfo.IsRequired && string.IsNullOrWhiteSpace((string)formInput))
						{
							errorMessage = $"{propLabel} is required.";
							break;
						}
					}
					else if (propertyInfo.PropertyType == typeof(int) || propertyInfo.PropertyType == typeof(int?))
					{
						int intValue = 0;
						if (int.TryParse(formInput, out intValue))
						{
							propertyInfo.SetValue(instance, intValue);
						}
						else
						{
							if (formInput != "")
							{
								errorMessage = $"{propLabel} incorrect format";
								break;
							}
							if (propDisplayInfo.IsRequired)
							{
								errorMessage = $"{propLabel} is required.";
								break;
							}
							if (propertyInfo.PropertyType == typeof(int?))
							{
								propertyInfo.SetValue(instance, null);
								continue;
							}
						}

						propertyInfo.SetValue(instance, intValue);
					}
					else if (propertyInfo.PropertyType == typeof(DateTime) || propertyInfo.PropertyType == typeof(DateTime?))
					{
						var dateTimeValue = string.IsNullOrEmpty(formInput) ? DateTime.MinValue : DateTime.Parse(formInput, null, DateTimeStyles.RoundtripKind);

						if (dateTimeValue.Equals(DateTime.MinValue))
						{
							if (propDisplayInfo.IsRequired)
							{
								errorMessage = $"{propLabel} is required.";
								break;
							}
							if (propertyInfo.PropertyType == typeof(DateTime?))
							{
								propertyInfo.SetValue(instance, null);
								continue;
							}
						}
						propertyInfo.SetValue(instance, dateTimeValue);
					}
					else if (propertyInfo.PropertyType == typeof(bool))
					{
						propertyInfo.SetValue(instance, formInput == "on");
					}
					else if (propertyInfo.PropertyType.IsEnum || (propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>) && propertyInfo.PropertyType.GetGenericArguments()[0].IsEnum))
					{						
						Type el = null;

						try
						{
							el = propertyInfo.PropertyType;

							if ((propertyInfo.PropertyType.IsGenericType && el.GetGenericTypeDefinition() == typeof(Nullable<>) && propertyInfo.PropertyType.GetGenericArguments()[0].IsEnum))
							{
								el = propertyInfo.PropertyType.GetGenericArguments()[0];
							}

							propertyInfo.SetValue(instance, Enum.Parse(el, getFormVariable(parentId)));
						}
						catch
						{
							var value = (propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>) && propertyInfo.PropertyType.GetGenericArguments()[0].IsEnum) ?
								null : GetDefaultEnumValue(el);

							propertyInfo.SetValue(instance, value);
						}
					}
					else if (propertyInfo.PropertyType.IsClass || (propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>) && propertyInfo.PropertyType.GetGenericArguments()[0].IsClass))
					{
						Type innerType = propertyInfo.PropertyType;
						if ((propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>) && propertyInfo.PropertyType.GetGenericArguments()[0].IsClass))
						{
							innerType = propertyInfo.PropertyType.GetGenericArguments()[0];
						}
						if (!nestedTypes.Add(innerType)) { continue; } //Circular reference, not allowed
						var nestedInstance = ProcessType(propId, innerType, getFormVariable, nestedTypes, out errorMessage);
						nestedTypes.Remove(innerType);

						propertyInfo.SetValue(instance, nestedInstance);
					}
					else if (!propertyInfo.PropertyType.IsValueType)
					{
						if (formInput == null || formInput.Length == 0)
						{
							propertyInfo.SetValue(instance, null);
							if (propDisplayInfo.IsRequired)
							{
								errorMessage = $"{propLabel} is required.";
								break;
							}
						}
						else
						{
							propertyInfo.SetValue(instance, JsonConvert.DeserializeObject(formInput, propertyInfo.PropertyType));
						}
					}
					else
					{
						propertyInfo.SetValue(instance, formInput);
					}
				}

				return instance;
			}

			errorMessage = $"Unable to process type {parentType.Name} for {parentId}";
			return null;
		}

		/// <summary>
		/// Enums doesn't have a default value, this method it returns the first value of the enum.
		/// The first value of an enum is the lowest positive integer (or the negative integer with the greatest absolute value less than zero), which is usually 0.
		/// so if you have overridden the values of the enum it may not return the expected value.
		/// if enum has duplicated values, is not guaranteed to return the first defined value.
		/// https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1069
		/// </summary>
		/// <param name="enumType"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentException"></exception>
		public static object GetDefaultEnumValue(Type enumType)
		{
			if (!enumType.IsEnum)
				throw new ArgumentException("Type must be an enum.");

			//rare case where the enum has no defined values.
			var names = Enum.GetNames(enumType);
			if (names.Length == 0)
				throw new InvalidOperationException("Enum type has no defined keys.");

			return Enum.GetValues(enumType).GetValue(0);
		}
	}
}
