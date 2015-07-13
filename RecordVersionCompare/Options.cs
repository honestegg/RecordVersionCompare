using CommandLine;
using MongoDB.Bson;
using MongoDB.Driver;

namespace RecordVersionCompare
{
    public class Options
    {
        private const string DefaultScheme = "mongodb://";
        private const string DefaultPort = "27017";

        [Option('h', "host")]
        public string Host { get; set; }

        private string HostOrDefault
        {
            get { return string.IsNullOrWhiteSpace(Host) ? "localhost" : Host; }
        }

        [Option('d', "db")]
        public string Database { get; set; }

        public string ConnectionString
        {
            get { return DefaultScheme + HostOrDefault + ":" + DefaultPort; }
        }

        public MongoServer GetServer()
        {
            var mongoUrlBuilder = new MongoUrlBuilder(ConnectionString)
            {
                ReadPreference = ReadPreference.SecondaryPreferred
            };

            var client = new MongoClient(mongoUrlBuilder.ToMongoUrl());
            return client.GetServer();
        }

        public MongoDatabase GetDatabase()
        {
            return GetServer().GetDatabase(Database);
        }

        public MongoCollection<BsonDocument> GetCollection(string collectionName)
        {
            return GetDatabase().GetCollection(collectionName);
        } 
    }
}
