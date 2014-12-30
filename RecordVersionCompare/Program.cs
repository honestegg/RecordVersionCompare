using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommandLine;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Bson.Serialization;

namespace RecordVersionCompare
{
    class Program
    {
        private static readonly Options Options = new Options();

        private static MongoCollection<BsonDocument> _collection;
        private static IMongoQuery _query;
        private static IMongoSortBy _sort;

        private static readonly JsonWriterSettings DefaultJsonWriterSettings = new JsonWriterSettings
        {
            Indent = true
        };

        private static readonly string BaseTempFolder = AppDomain.CurrentDomain.BaseDirectory + "/temp";
        private static readonly string CompareToolPath = ConfigurationManager.AppSettings["compareToolPath"];

        private static IMongoQuery QueryOrDefault
        {
            get { return _query ?? Query.Exists("_id"); }
        }

        private static IMongoSortBy SortOrDefault
        {
            get { return _sort ?? SortBy.Ascending(); }
        }

        private static readonly Dictionary<string, Action<string>> Commands = new Dictionary<string, Action<string>>
        {
            {"set", Set},
            {"find", Find},
            {"sort", Sort},
            {"compare", Compare}
        };

        static void Main(string[] args)
        {
            if (Directory.Exists(BaseTempFolder))
            {
                foreach (var filePath in Directory.GetFiles(BaseTempFolder))
                {
                    File.Delete(filePath);
                }
            }
            Directory.CreateDirectory(BaseTempFolder);

            Parser.Default.ParseArguments(args, Options);
            PrintServerAndDatabase();
            WaitForCommand();
        }

        static void Set(string input)
        {
            Parser.Default.ParseArguments(input.Split(' '), Options);
            PrintServerAndDatabase();
            PrintCurrentQuery();
        }

        static void Find(string input)
        {
            var args = input.SplitTwo();
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                _collection = Options.GetCollection(args[0]);
                _query = args.Length > 1 ? JsonToQuery(args[1]) : null;
            }

            PrintCurrentQuery();
            
            if (_collection != null)
            {
                var cnt = _collection.Count(_query);
                Console.WriteLine("Total results: {0}", cnt);
                if (ConfirmContinue("Display results?"))
                {
                    var cursor = _collection.Find(QueryOrDefault)
                        .SetSortOrder(SortOrDefault)
                        .SetLimit(10);

                    PrintResults(cursor);
                }
            }
        }

        static void Sort(string input)
        {
            if (!string.IsNullOrWhiteSpace(input))
            {
                _sort = JsonToSort(input);
            }

            PrintCurrentQuery();
        }

        private static void Compare(string input)
        {
            PrintCurrentQuery();

            if (_collection == null)
            {
                return;
            }

            var cnt = _collection.Count(_query);
            Console.WriteLine("Total results: {0}", cnt);

            if (cnt < 2)
            {
                Console.WriteLine("Not enough results to compare.");
                return;
            }

            var cursor = _collection.Find(QueryOrDefault)
                .SetSortOrder(SortOrDefault);

            var randomText = GenerateRandomText();

            var file2 = string.Empty;
            foreach (var record in cursor)
            {
                var file1 = file2;
                file2 = string.Format("{0}\\{1}.js", BaseTempFolder, BuildFileName(_collection.Name, record, randomText));

                File.WriteAllText(file2, SortElements(record).ToJson(DefaultJsonWriterSettings));

                if (file1 == string.Empty || file2 == string.Empty)
                {
                    continue;
                }

                Console.WriteLine("Comparing records...");
                Process.Start(CompareToolPath, string.Format("\"{0}\" \"{1}\"", file1, file2));
                if (!ConfirmContinue("Go to next set?"))
                {
                    return;
                }
            }
        }

        static string GenerateRandomText()
        {
            var newGuid = Guid.NewGuid();
            var newGuid64 = Convert.ToBase64String(newGuid.ToByteArray());
            var randomText = newGuid64
                .Replace("/", "")
                .Replace("+", "")
                .ToLowerInvariant()
                .Substring(0, 4);

            return randomText;
        }

        private static string BuildFileName(string collectionName, BsonDocument record, string randomText)
        {
            var id = record["_id"];
            BsonValue entityId = null; 
            DateTime? updatedDate = null;
            
            BsonValue audit;
            if (record.TryGetValue("Adt", out audit))
            {
                updatedDate = audit.AsBsonDocument["UT"].ToUniversalTime();
            }
            else if (record.Contains("SaveTime"))
            {
                updatedDate = record["SaveTime"].ToUniversalTime();
            }

            if (record.Contains("EntityId"))
            {
                entityId = record["EntityId"];
            }

            var filename = collectionName;

            if (id.IsNumeric)
            {
                filename += "-id" + id;
            }
            else if (entityId != null && entityId.IsNumeric)
            {
                filename += "-id" + entityId;
            }

            if (updatedDate.HasValue)
            {
                filename += "-" + updatedDate.Value.ToString("yyyyMMdd_HHmmss_fff");
            }

            return filename + "-" + randomText;
        }

        static BsonDocument SortElements(BsonDocument doc)
        {
            var sortedDoc = new BsonDocument();
            foreach (var e in doc.Elements.OrderBy(e => e.Name))
            {
                var name = e.Name;
                var value = e.Value;
                if (value.IsBsonDocument)
                {
                    value = SortElements(value.AsBsonDocument);
                }
                sortedDoc.Add(name, value);
            }

            return sortedDoc;
        }

        static void PrintResults(IEnumerable<BsonDocument> cursor)
        {
            foreach (var item in cursor)
            {
                Console.WriteLine(item.ToJson(DefaultJsonWriterSettings));
            }
        }

        static IMongoQuery JsonToQuery(string json)
        {
            var document = BsonSerializer.Deserialize<BsonDocument>(json);
            return new QueryDocument(document);
        }

        static IMongoSortBy JsonToSort(string json)
        {
            var document = BsonSerializer.Deserialize<BsonDocument>(json);
            return new SortByDocument(document);
        }

        static void PrintServerAndDatabase()
        {
            Console.WriteLine("Server: {0}", Options.ConnectionString);
            Console.WriteLine("Database: {0}", Options.Database);
        }

        private static void PrintCurrentQuery()
        {
            Console.WriteLine("Collection: {0}", _collection != null ? _collection.Name : string.Empty);
            Console.WriteLine("Query: {0}", _query != null ? _query.ToJson() : string.Empty);
            Console.WriteLine("Sort: {0}", _sort != null ? _sort.ToJson() : string.Empty);
            Console.WriteLine("");
        }

        private static void WaitForCommand()
        {
            while (true)
            {
                Console.Write("> ");
                Console.Out.Flush();
                var input = Console.ReadLine();

                var args = input.SplitTwo();

                if (args[0] == "exit")
                {
                    Console.WriteLine("Exiting...");
                    return;
                }

                if (Commands.ContainsKey(args[0]))
                {
                    var i = args.Length > 1 ? args[1] : string.Empty;
                    Commands[args[0]](i);
                }
                else
                {
                    PrintServerAndDatabase();
                    PrintCurrentQuery();
                }
            }
        }

        static bool ConfirmContinue(string message)
        {
            Console.Write(message + " [y/n]: ");
            var input = Console.ReadLine();
            return input != null && input.ToLowerInvariant() == "y";
        }
    }
}
