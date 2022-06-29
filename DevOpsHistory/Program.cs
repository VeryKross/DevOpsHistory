using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using CommonLib;
using Microsoft.TeamFoundation.Common;

namespace AzureApiTest
{
    class Program
    {
        private static string _personalAccessToken;
        private const string PatKey = "DevOpsHistoryInfo";
        private const string OrgKey = "DevOpsOrgId";
        private const string LocKey = "OutputLocation";

        static void Main(string[] args)
        {
            Console.WriteLine($"DevOps History Reporter v1.5");
            Console.WriteLine("=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=");
            Console.WriteLine("");

            if (args.Length == 1 && (args[0] == "?" || args[0].ToLower(CultureInfo.CurrentCulture) == "help"))
            {
                Console.WriteLine("Supported Parameters:");
                Console.WriteLine();
                Console.WriteLine("?/Help:       to see this message - congratulations!");
                Console.WriteLine("Clear:        Used to clear some or all saved parameters (e.g. Clear, Clear org, Clear pat, Clear loc)");
                Console.WriteLine("-----------------------------------------------------------");
                Console.WriteLine("Note that you will need to supply a Personal Access Token (PAT) the first time you use the utility.");
                Console.WriteLine("The PAT will be saved locally for subsequent use unless you opt out of storing it (in which case you'll be asked for it on each run).");

                return;
            }

            // Get the current configuration file.
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            // Check to see if the user has asked for one or all of the stored values to be cleared.
            if (args.Length > 0 && args[0].ToLower(CultureInfo.CurrentCulture) == "clear")
            {
                if (args.Length == 1)
                {
                    config.AppSettings.Settings.Remove(PatKey);
                    config.AppSettings.Settings.Remove(OrgKey);
                    config.AppSettings.Settings.Remove(LocKey);
                }
                else
                {
                    switch (args[1].ToLower(CultureInfo.CurrentCulture))
                    {
                        case "org":
                            config.AppSettings.Settings.Remove(OrgKey);
                            break;

                        case "pat":
                            config.AppSettings.Settings.Remove(PatKey);
                            break;

                        case "loc":
                            config.AppSettings.Settings.Remove(LocKey);
                            break;
                    }
                }
            }


            if (config.AppSettings.Settings[PatKey] == null)
            {
                Console.WriteLine("Enter your Personal Access Token (PAT):");
                _personalAccessToken = Console.ReadLine();
                Console.WriteLine($@"Type ""YES"" to remember your PAT.");
                var saveIt = Console.ReadLine();

                if (saveIt?.ToUpper(CultureInfo.CurrentCulture) == "YES")
                {
                    config.AppSettings.Settings.Add(PatKey, _personalAccessToken);
                    config.Save();
                }
            }
            else
            {
                _personalAccessToken = config.AppSettings.Settings[PatKey].Value;
            }
            
            var taskId = 0;
            var orgId = "";
            var filePath = "";

            if (config.AppSettings.Settings[OrgKey] == null)
            {
                Console.WriteLine("Enter your organization ID:");
                orgId = Console.ReadLine();

                Console.WriteLine($@"Type ""YES"" to remember your organization.");
                var saveIt = Console.ReadLine();

                if (saveIt?.ToUpper(CultureInfo.CurrentCulture) == "YES")
                {
                    config.AppSettings.Settings.Add(OrgKey, orgId);
                    config.Save();
                }
            }
            else
            {
                orgId = config.AppSettings.Settings[OrgKey].Value;
            }

            if (config.AppSettings.Settings[LocKey] == null)
            {
                Console.WriteLine(@"Enter the full path to the folder where you want the history file to be stored (e.g. 'C:\MyFiles'):");
                filePath = Console.ReadLine();

                Console.WriteLine($@"Type ""YES"" to remember your folder location.");
                var saveIt = Console.ReadLine();

                if (saveIt?.ToUpper(CultureInfo.CurrentCulture) == "YES")
                {
                    config.AppSettings.Settings.Add(LocKey, filePath);
                    config.Save();
                }
            }
            else
            {
                filePath = config.AppSettings.Settings[LocKey].Value;
            }

            if (!Directory.Exists(filePath))
            {
                try
                {
                    var di = Directory.CreateDirectory(filePath);
                    Console.WriteLine($"New directory '{di.FullName}' has been created.");
                    Console.WriteLine();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while attempting to create output folder '{filePath}': {e.Message}");
                    Console.WriteLine("Press ENTER to exit.");
                    Console.ReadLine();
                    return;
                }
            }

            Console.WriteLine("Enter the ID of the ADO Work Item to retrieve (e.g. 3421):");
            var taskIdInput = Console.ReadLine();
            Console.WriteLine();

            if (!taskIdInput.IsNullOrEmpty())
            {
                try
                {
                    taskId = Convert.ToInt32(taskIdInput);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while validating Work Item ID: {e.Message}");
                    Console.WriteLine("A valid ADO work item ID (e.g. 3421) is required to continue.");
                    Console.WriteLine("Press ENTER to exit.");
                    Console.ReadLine();
                    return;
                }
            }
            else
            {
                Console.WriteLine("A valid ADO work item ID (e.g. 3421) is required to continue.");
                Console.WriteLine("Press ENTER to exit.");
                Console.ReadLine();
                return;
            }

            var histDude = new HistoryTools(_personalAccessToken, taskId, orgId);

            var changes = histDude.GetChangeHistory();

            WriteList($@"{filePath}\{taskId}-Changes.txt", changes, taskId);
        }
        
        /// <summary>
        /// Write the formatted history scan results to the specified output file.
        /// </summary>
        /// <param name="fileName">The name of the file to create.</param>
        /// <param name="listData">The list of key-value pairs representing the history scan results.</param>
        /// <param name="workItemId">The ID of the work item this scan is for.</param>
        public static void WriteList(string fileName, List<(string Key, string Value)> listData, int workItemId)
        {
            using (var writer = new StreamWriter(fileName))
            {
                DateTime theTime = DateTime.Now;
                var timeZone = theTime.IsDaylightSavingTime()
                    ? TimeZoneInfo.Local.DaylightName
                    : TimeZoneInfo.Local.StandardName;

                // List of keys that typically have very long string content and limited value in outputting.
                List<string> skipKeys = new List<string>()
                {
                    "Custom.AssessmentOutcomeReason", "Custom.ModernizationStatusNotes", "System.History", "System.Description", "Custom.OptimizationStatusNotes", "Custom.ProgressNotes"
                };

                writer.WriteLine($"History as of {theTime.ToString(CultureInfo.CurrentCulture)} ({timeZone}) for ID {workItemId}");
                writer.WriteLine("Values in [brackets] represent the value prior to this revision.");
                writer.WriteLine(new string(':', 80));

                foreach (var valuePair in listData)
                {
                    if (!skipKeys.Contains(valuePair.Key))
                    {
                        writer.WriteLine(valuePair.Key == "" ? valuePair.Value : $"{valuePair.Key}: {valuePair.Value}");
                    }
                    else
                    {
                        // Don't output these long strings, just indicate that a value was present (otherwise it makes reading the rest of the history difficult)
                        writer.WriteLine($"{valuePair.Key}: ...long text skipped...");
                    }
                }
            }

            Console.WriteLine(new string('-',70));
            Console.WriteLine($"Output saved in file: {fileName}");
            Console.WriteLine();
        }

    }
}
