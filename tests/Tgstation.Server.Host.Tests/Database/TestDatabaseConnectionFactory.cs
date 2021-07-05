using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySql.Data.MySqlClient;
using System;
using System.Data.SqlClient;

using Tgstation.Server.Host.Configuration;

namespace Tgstation.Server.Host.Database.Tests
{
	[TestClass]
	public sealed class TestDatabaseConnectionFactory
	{
		[TestMethod]
		public void TestBadParameters()
		{
			var factory = new DatabaseConnectionFactory();
			Assert.ThrowsException<ArgumentNullException>(() => factory.CreateConnection(null, default));
			Assert.ThrowsException<ArgumentOutOfRangeException>(() => factory.CreateConnection(String.Empty, (DatabaseType)42));
		}

		[TestMethod]
		public void TestWorks()
		{
			var factory = new DatabaseConnectionFactory();
			Assert.IsInstanceOfType(factory.CreateConnection(String.Empty, DatabaseType.MariaDB), typeof(MySqlConnection));
			Assert.IsInstanceOfType(factory.CreateConnection(String.Empty, DatabaseType.MySql), typeof(MySqlConnection));
			Assert.IsInstanceOfType(factory.CreateConnection(String.Empty, DatabaseType.SqlServer), typeof(SqlConnection));
		}
	}
}
