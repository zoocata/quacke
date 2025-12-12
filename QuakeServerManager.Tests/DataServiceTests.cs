using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuakeServerManager.Services;
using QuakeServerManager.Models;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

namespace QuakeServerManager.Tests
{
    [TestClass]
    public class DataServiceTests
    {
        private const string TestProfileDirectory = "test_profiles";

        [TestInitialize]
        public void TestInitialize()
        {
            if (Directory.Exists(TestProfileDirectory))
            {
                Directory.Delete(TestProfileDirectory, true);
            }
            Directory.CreateDirectory(TestProfileDirectory);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (Directory.Exists(TestProfileDirectory))
            {
                Directory.Delete(TestProfileDirectory, true);
            }
        }

        [TestMethod]
        public async Task SaveAndLoadVpsConnection_ShouldSucceed()
        {
            // Arrange
            var dataService = new DataService();
            var vpsConnection = new VpsConnection
            {
                Name = "TestVps",
                Ip = "127.0.0.1",
                Port = 22,
                Username = "root",
                Password = "password"
            };

            // Act
            await dataService.SaveVpsConnectionAsync(vpsConnection);
            var loadedConnections = await dataService.LoadVpsConnectionsAsync();
            var loadedConnection = loadedConnections.FirstOrDefault(c => c.Name == "TestVps");

            // Assert
            Assert.IsNotNull(loadedConnection);
            Assert.AreEqual(vpsConnection.Name, loadedConnection.Name);
            Assert.AreEqual(vpsConnection.Ip, loadedConnection.Ip);
            Assert.AreEqual(vpsConnection.Port, loadedConnection.Port);
            Assert.AreEqual(vpsConnection.Username, loadedConnection.Username);
            Assert.AreEqual(vpsConnection.Password, loadedConnection.Password);
        }
    }
}
