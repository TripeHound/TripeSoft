using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TripeSoft.Utils;

namespace Tests
{
	/// <summary>
	/// Summary description for UnitTest1
	/// </summary>
	[TestClass]
	public class PartitionTests
	{
		Partitioner.Partition unassigned = new Partitioner.Partition( 10, 100, Partitioner.Partition.States.Unassigned );
		Partitioner.Partition  tentative = new Partitioner.Partition( 10, 100, Partitioner.Partition.States.Assigned);//WRONG!
		Partitioner.Partition   assigned = new Partitioner.Partition( 10, 100, Partitioner.Partition.States.Assigned );

		public PartitionTests()
		{
			//
			// TODO: Add constructor logic here
			//
		}

		private TestContext testContextInstance;

		/// <summary>
		///Gets or sets the test context which provides
		///information about and functionality for the current test run.
		///</summary>
		public TestContext TestContext
		{
			get
			{
				return testContextInstance;
			}
			set
			{
				testContextInstance = value;
			}
		}

		#region Additional test attributes
		//
		// You can use the following additional attributes as you write your tests:
		//
		// Use ClassInitialize to run code before running the first test in the class
		// [ClassInitialize()]
		// public static void MyClassInitialize(TestContext testContext) { }
		//
		// Use ClassCleanup to run code after all tests in a class have run
		// [ClassCleanup()]
		// public static void MyClassCleanup() { }
		//
		// Use TestInitialize to run code before running each test 
		// [TestInitialize()]
		// public void MyTestInitialize() { }
		//
		// Use TestCleanup to run code after each test has run
		// [TestCleanup()]
		// public void MyTestCleanup() { }
		//
		#endregion

		private void ShouldNotContain( Partitioner partitioner, long offset )
		{
			try
			{
				Partitioner.Partition partition = partitioner.ContainingPartition( offset );
				Assert.Fail( "Offset {0} found in {1}", new object[] { offset, partition } );
			}
			catch( ArgumentOutOfRangeException ex )
			{
				StringAssert.Contains( ex.Message, Partitioner.ExMsg.NotInParentPartition );
			}
		}

		[TestMethod]
		public void TestPartition_IsXXX()
		{
			Assert.IsTrue	( unassigned.IsUnassigned,	"Unassigned partition is not unaassigned"	);
			Assert.IsTrue	( unassigned.IsTruncatable,	"Unassigned partition is not truncatable"	);
			Assert.IsFalse	( unassigned.IsTentative,	"Unassigned partition is tentative"			);
			Assert.IsFalse	( unassigned.IsAssigned,	"Unassigned partition is assigned"			);
			Assert.IsFalse	( tentative.IsUnassigned,	"Tentative partition is unassigned"			);
			Assert.IsTrue	( tentative.IsTruncatable,	"Tentative partition is not truncatable"	);
			Assert.IsTrue	( tentative.IsTentative,	"Tentative partition is not tentative"		);
			Assert.IsFalse	( tentative.IsAssigned,		"Tentative partition is assigned"			);
			Assert.IsFalse	( assigned.IsUnassigned,	"Assigned partition is unassigned"			);
			Assert.IsFalse	( assigned.IsTruncatable,	"Assigned partition is truncatable"			);
			Assert.IsFalse	( assigned.IsTentative,		"Assigned partition is tentative"			);
			Assert.IsTrue	( assigned.IsAssigned,		"Assigned partition is not assigned"		);
		}

		[TestMethod]
		public void TestPartition_MarkAssigned()
		{
			try
			{
				unassigned.MarkAssigned();
			}
			catch( Exception ex )
			{
				Assert.Fail( "Unassigned partition could not be marked assigned: " + ex.Message );
			}
			try
			{
				tentative.MarkAssigned();
			}
			catch( Exception ex )
			{
				Assert.Fail( "Tentative partition could not be marked assigned: " + ex.Message );
			}
			try
			{
				assigned.MarkAssigned();
				Assert.Fail( "Assigned partiton could be marked assigned" );
			}
			catch( InvalidOperationException ex )
			{
				StringAssert.Contains( Partitioner.ExMsg.PartitionAlreadyAssigned, ex.Message );
			}
		}

		[TestMethod]
		public void TestContain()
		{
			const long Size = 100 ;
			Partitioner partitioner = new Partitioner( Size );
			ShouldNotContain( partitioner, -1 );
			ShouldNotContain( partitioner, Size );

			Assert.AreNotEqual	( null, partitioner.ContainingPartition(      0 ), "Beginging is not contained" );
			Assert.AreNotEqual	( null, partitioner.ContainingPartition( Size-1 ), "End is not contained" );

			System.Diagnostics.Debug.WriteLine( partitioner.ToString() );
		}
	}
}
