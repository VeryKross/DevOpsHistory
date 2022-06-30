using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.Common;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace CommonLib
{
    public class HistoryTools
    {
        private const string ApiRootUri = "https://dev.azure.com/";
        private const string ChangedByField = "System.ChangedBy";
        private const string ChangedDateField = "System.ChangedDate";
        private const string EmptyValueIndicator = "*empty*";

        private readonly Dictionary<string, string> _masterKeys;
        private readonly List<string> _skipKeys;

        private bool _valueTracking = false;
        private string _trackField;
        private string _firstVal = string.Empty;
        private string _lastVal = string.Empty;

        private string ApiUri { get; set; }
        private int WorkItemId { get; set; }
        private string PersonalAccessToken { get; set; }

        public HistoryTools(string pat, int workItemId, string org)
        {
            _masterKeys = new Dictionary<string, string>();

            PersonalAccessToken = pat;
            WorkItemId = workItemId;
            ApiUri = ApiRootUri + org;

            _skipKeys = new List<string>() { "System.Rev", "System.Watermark", ChangedByField, ChangedDateField };
        }


        /// <summary>
        /// Returns a list of Key/Value pairs documenting all field changes over the entire
        /// history of the item. Optionally, writes out the detail field values from each revision.
        /// </summary>
        /// <param name="writeDetail">If TRUE, a text file will be created with the details from each revision.</param>
        /// <param name="detailPath">Folder to write the revision detail files to.</param>
        /// <param name="trackField">Option field name to limiting change tracking to a single named field.</param>
        /// <returns>List of key/value pairs with field names and values representing changes in each revision.</returns>
        public List<(string Key, string Value)> GetChangeHistory(bool writeDetail = false, string detailPath = "", string trackField = "")
        {
            Console.WriteLine($"Getting Change History for item {WorkItemId}...");

            var result = new List<(string Key, string Value)>();
            var sectionSeparator = new string('=', 60);
            var headingSeparator = new string('-', 30);

            _valueTracking = !string.IsNullOrEmpty(trackField);
            _trackField = trackField;

            var allRevisions = RetrieveRevisionHistory();


            Console.WriteLine("Evaluating changes over each revision...");
            // Look for changes from one revision to the next
            foreach (var revisionItem in allRevisions)
            {
                if (writeDetail && !detailPath.IsNullOrEmpty())
                {
                    WriteRevision(revisionItem, detailPath, 100);
                }

                var revHeading = new List<(string Key, string Value)>();
                var delta = GetDelta(revisionItem.Fields, revisionItem.Rev > 1);

                if (delta.Count > 0)
                {
                    if (!_valueTracking)
                    {
                        var changedDate = revisionItem.Fields.First(i => i.Key == ChangedDateField).Value + " UTC";
                        var changedBy = (IdentityRef)revisionItem.Fields.First(i => i.Key == ChangedByField).Value;
                        var revId = revisionItem.Rev == 1
                            ? $"{revisionItem.Rev} - New Record"
                            : revisionItem.Rev.ToString();

                        if (revisionItem.Rev > 1)
                            revHeading.Add(new("", sectionSeparator));

                        revHeading.Add(new("Revision", revId));
                        revHeading.Add(new("Changed Date", changedDate));
                        revHeading.Add(new("Changed By", $"{changedBy.DisplayName} ({changedBy.UniqueName})"));
                        revHeading.Add(new("", headingSeparator));

                        result.AddRange(revHeading);
                    }

                    result.AddRange(delta);
                }
            }

            Console.WriteLine($"Evaluated {allRevisions.Count} revisions to item {WorkItemId}.");

            return result;
        }

        /// <summary>
        /// Compare the current revision field list against the last known state in the MasterKeys dictionary.
        /// </summary>
        /// <param name="fields">The complete list of fields (key/value pairs) for a given revision.</param>
        /// <param name="includeOldValue">If TRUE, the delta output will include both the new value and the old.</param>
        /// <returns></returns>
        private List<(string Key, string Value)> GetDelta(IDictionary<string, object> fields, bool includeOldValue)
        {
            var result = new List<(string Key, string Value)>();

            foreach (var field in fields)
            {
                if ((_valueTracking && field.Key == _trackField) || !_valueTracking)
                {
                    var newVal = field.Value.ToString();

                    if (_valueTracking && string.IsNullOrEmpty(_firstVal)) _firstVal = newVal;
                    if (_valueTracking) _lastVal = newVal;

                    // Build up the master list of keys as we go.
                    if (!_masterKeys.ContainsKey(field.Key))
                    {
                        _masterKeys.Add(field.Key, "");
                    }

                    var oldVal = _masterKeys[field.Key];

                    if (!_skipKeys.Contains(field.Key))
                    {
                        if (oldVal != newVal)
                        {
                            var outNew = !newVal.IsNullOrEmpty() ? newVal : EmptyValueIndicator;
                            var outOld = !oldVal.IsNullOrEmpty() ? oldVal : EmptyValueIndicator;

                            if (!_valueTracking)
                            {
                                if (includeOldValue)
                                {
                                    result.Add(new($"{field.Key}", $"{outNew} [{outOld}]"));
                                }
                                else
                                {
                                    result.Add(new($"{field.Key}", $"{outNew}"));
                                }
                            }

                            // Record the new value for this key.
                            _masterKeys[field.Key] = newVal;
                        }
                    }

                }
            }

            // This loop is necessary because removal of a field is sometimes represented by the field
            // no longer being included in the list of fields within a rev. The first time we're not
            // able to find a previously identified Key, we mark it as having been set to *empty*.
            foreach (var masterKey in _masterKeys)
            {
                var oldVal = masterKey.Value;

                if (!fields.ContainsKey(masterKey.Key) && masterKey.Value != EmptyValueIndicator)
                {
                    _masterKeys[masterKey.Key] = EmptyValueIndicator;
                    if (!_valueTracking)
                    {
                        result.Add(new($"{masterKey.Key}", $"{EmptyValueIndicator} [{oldVal}]"));
                    }
                    else
                    {
                        _lastVal = EmptyValueIndicator;
                    }
                }
            }

            if (_valueTracking)
            {
                result.Add(new($"{_firstVal}", $"{_lastVal}"));
            }

            return result;
        }

        /// <summary>
        /// Retrieve all history for the specified work item and return the complete key/value list.
        /// </summary>
        /// <returns></returns>
        private List<WorkItem> RetrieveRevisionHistory()
        {
            Console.Write("Retrieving full history...");

            var skipCount = 0;
            var batch = 1;
            var allRevisionItems = new List<WorkItem>();
            var apiMax = 0;

            try
            {
                VssConnection connection = new VssConnection(new Uri(ApiUri), new VssBasicCredential(string.Empty, PersonalAccessToken));
                WorkItemTrackingHttpClient witClient = connection.GetClient<WorkItemTrackingHttpClient>();

                // Retrieve all of the revisions
                while (skipCount >= 0)
                {
                    Console.Write($"{batch}...");

                    // Retrieve up to a maximum of 200 revisions
                    var revisionItems =
                        witClient.GetRevisionsAsync(id: WorkItemId, skip: skipCount, expand: WorkItemExpand.All).Result;

                    if(revisionItems.Count > 0) allRevisionItems.AddRange(revisionItems);

                    // ADO APIs typically cap the # of records they're return on a single call (e.g. 200) so we'll assume that
                    // the count we retrieve on the first call is the maximum we can retrieve and use that to calculate
                    // a skip value so we can retrieve all records in multiple "batches" of max count records. In some
                    // cases, all records will be received in the first call and so we'll receive nothing in the next.
                    if (apiMax == 0) apiMax = revisionItems.Count;

                    skipCount = revisionItems.Count == apiMax ? batch * apiMax : -1;
                    batch += 1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.WriteLine($"Exception in RetrieveHistory: {e.Message}");
                Console.WriteLine();
            }

            Console.WriteLine("done.");
            return allRevisionItems;
        }

        /// <summary>
        /// This method can be used to "dump" the key/value pairs for a Revision so it can be
        /// examined in more detail. This is triggered through parameters passed to the
        /// GetChangeHistory method.
        /// </summary>
        /// <param name="workItem">This represents the state of a work item at a revision point, including all fields.</param>
        /// <param name="folderName">This is the name of the folder to write the output file to.</param>
        /// <param name="lengthLimit">An optional maximum value length after which it will be truncated on output.</param>
        private void WriteRevision(WorkItem workItem, string folderName, int lengthLimit = 0)
        {
            var fileName = Path.Combine(folderName, $"{WorkItemId}-{workItem.Rev}_Details.txt");
            using (var writer = new StreamWriter(fileName))
            {
                writer.WriteLine($"Detail output for work item ID {WorkItemId}, revision {workItem.Rev}");
                writer.WriteLine(new string('=', 80));

                foreach (var valuePair in workItem.Fields)
                {
                    // For fields with a value that exceeds the specified length limit, the value it truncated and appended to indicate the extra text has been skipped.
                    if (lengthLimit > 0 && valuePair.Value.ToString()!.Length > lengthLimit)
                    {
                        writer.WriteLine($"{valuePair.Key}: {valuePair.Value.ToString()?[..lengthLimit]}...long text skipped...");
                    }
                    else
                    {
                        writer.WriteLine(valuePair.Key == "" ? valuePair.Value : $"{valuePair.Key}: {valuePair.Value}");
                    }
                }
            }
        }
    }
}
