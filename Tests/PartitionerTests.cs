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
		//TODO: These probably shouldn't be global/static: probably ought to create what we need in each test...
		//
		static Partitioner.Partition unassigned ;
		static Partitioner.Partition  tentative ;
		static Partitioner.Partition   assigned ;

		public TestContext TestContext { get; set; }

		public PartitionTests()
		{
		}

		[TestInitialize]
		public static void TestInitialise()
		{
			unassigned = new Partitioner.Partition( 10, 90, Partitioner.Partition.States.Unassigned );
			tentative = new Partitioner.Partition( 10, 90, Partitioner.Partition.States.Tentative );
			assigned = new Partitioner.Partition( 10, 90, Partitioner.Partition.States.Assigned );
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

		#region Partiton.Contructor tests
		//--------------------------------------------------------------------------------------------------------------------
		//	Class	Partition
		//	Item	Constructor
		//	Tests	Correct objects are created
		//
		[TestMethod]
		public void TestPartition_Constructor()
		{
			long ExpectedOffset = 10;
			long ExpectedLength = 100;
			Partitioner.Partition.States ExpectedState = Partitioner.Partition.States.Assigned;

			Partitioner.Partition partition = new Partitioner.Partition( ExpectedOffset, ExpectedLength, ExpectedState );

			Assert.AreEqual( ExpectedOffset, partition.Offset, "Offset is incorrect" );
			Assert.AreEqual( ExpectedLength, partition.Length, "Length is incorrect" );
			Assert.AreEqual( ExpectedState,  partition.State,  "State is incorrect" );
		}
		#endregion

		#region Partition.IsXXX tests
		//--------------------------------------------------------------------------------------------------------------------
		//	Class	Partion
		//	Item	IsXXX properties
		//	Tests	They give expected states for different partitions
		//
		[TestClass]
		public class IsXXX
		{
			[TestInitialize]
			public void TestInitialise()
			{
				PartitionTests.TestInitialise();
			}
			[TestMethod]
			public void TestPartition_IsXXX()
			{
				Assert.IsTrue( unassigned.IsUnassigned, "Unassigned partition is not unaassigned" );
				Assert.IsTrue( unassigned.IsTruncatable, "Unassigned partition is not truncatable" );
				Assert.IsFalse( unassigned.IsTentative, "Unassigned partition is tentative" );
				Assert.IsFalse( unassigned.IsAssigned, "Unassigned partition is assigned" );
				Assert.IsFalse( tentative.IsUnassigned, "Tentative partition is unassigned" );
				Assert.IsTrue( tentative.IsTruncatable, "Tentative partition is not truncatable" );
				Assert.IsTrue( tentative.IsTentative, "Tentative partition is not tentative" );
				Assert.IsFalse( tentative.IsAssigned, "Tentative partition is assigned" );
				Assert.IsFalse( assigned.IsUnassigned, "Assigned partition is unassigned" );
				Assert.IsFalse( assigned.IsTruncatable, "Assigned partition is truncatable" );
				Assert.IsFalse( assigned.IsTentative, "Assigned partition is tentative" );
				Assert.IsTrue( assigned.IsAssigned, "Assigned partition is not assigned" );
			}
		}
		#endregion

		#region Partition.MarkAssigned() tests
		[TestClass]
		public class MarkAssigned
		{
			[TestInitialize]
			public void TestInitialise()
			{
				PartitionTests.TestInitialise();
			}

			//--------------------------------------------------------------------------------------------------------------------
			//	Class	Partition
			//	Item	MarkAssigned()
			//	Tests	Unassigned		Should work
			//			Tentative		Should work
			//			Assigned		Should fail
			//
			[TestMethod]
			public void TestPartition_MarkAssigned_Unassigned()
			{
				unassigned.MarkAssigned();
			}
			[TestMethod]
			public void TestPartition_MarkAssigned_Tentative()
			{
				tentative.MarkAssigned();
			}
			[TestMethod]
			[ExpectedException( typeof( InvalidOperationException ), Partitioner.ExMsg.PartitionAlreadyAssigned )]
			public void TestPartition_MarkAssigned_Assigned()
			{
				assigned.MarkAssigned();
			}
		}
		#endregion

		#region Partition.TruncateBefore() tests
		[TestClass]
		public class TruncateBefore
		{
			[TestInitialize]
			public void TestInitialise()
			{
				PartitionTests.TestInitialise();
			}
			//--------------------------------------------------------------------------------------------------------------------
			//	Class	Partition
			//	Item	TruncateBefore()
			//	Tests	Assigned		Should fail
			//			BeforeStart		Should fail
			//			BeyondEnd		Should fail
			//			AtStart			Should fail (cannot truncate to nothing)
			//
			[TestMethod]
			[ExpectedException( typeof( InvalidOperationException ), Partitioner.ExMsg.PartitionNotTruncatable )]
			public void TestPartition_Truncate_Assigned()
			{
				assigned.TruncateBefore( 50 );
			}
			[TestMethod]
			[ExpectedException( typeof( ArgumentOutOfRangeException ), Partitioner.ExMsg.TruncationOutsidePartition )]
			public void TestPartition_Truncate_BeforeStart()
			{
				Partitioner.Partition partition = unassigned;
				partition.TruncateBefore( 9 );
			}
			[TestMethod]
			[ExpectedException( typeof( ArgumentOutOfRangeException ), Partitioner.ExMsg.TruncationOutsidePartition )]
			public void TestPartition_Truncate_BeyondEnd()
			{
				Partitioner.Partition partition = tentative;
				partition.TruncateBefore( 101 );
			}
			[TestMethod]
			[ExpectedException( typeof( ArgumentOutOfRangeException ), Partitioner.ExMsg.TruncationWouldDelete )]
			public void TestPartition_Truncate_AtStart()
			{
				Partitioner.Partition partition = unassigned;
				partition.TruncateBefore( 10 );
			}
			//--------------------------------------------------------------------------------------------------------------------
			//	Class	Partition
			//	Item	TruncateBefore()
			//	Helper	If the truncation point is at the end of the partition, no remainder should be generated.
			//			If the truncation point is before the end, a new unassigned remainder partition should be created
			//				(starting at the truncation point and the combined lengths should match the orginal length).
			//			If a 'Tentative' partition is truncated, it should be marked 'Assigned'.
			//			The original partition's offset should not change.
			//
			private void TestPartition_Truncate_Helper(
				Partitioner.Partition partition,
				long offset )
			{
				//Check
				Assert.IsNotNull( partition, "TestFail: No partition" );
				Assert.IsTrue( partition.ContainsOrNext( offset ), "TestFail: Offset not inside partition" );
				Assert.AreNotEqual( Partitioner.Partition.States.Assigned, partition.State, "TestFail: Partition is assigned" );

				//Prepare
				long originalOffset			= partition.Offset;
				long originalLength			= partition.Length;
				long originalNext			= partition.NextOffset;
				bool originalWasTentative	= partition.State == Partitioner.Partition.States.Tentative;
				bool expectRemainder		= offset < originalNext;

				//Act
				Partitioner.Partition remainder = partition.TruncateBefore( offset );

				//Assert
				//TODO	Apparently, all these checks should be in separate tests ... I can sort of see the reasons
				//		for doing so, but it's too much hassle to do so now (may be better once I've got the hand
				//		of data-source-driven testing: several tests with one assertion each, all driven from the
				//		same list of test-cases, perhaps).
				//
				Assert.AreEqual( originalOffset, partition.Offset, "Original offset has changed" );
				Assert.AreEqual( offset - originalOffset, partition.Length, "New length is incorrect" );
				if( originalWasTentative )
					Assert.AreEqual( Partitioner.Partition.States.Assigned, partition.State, "State of tentative partition not marked assigned" );
				else
					Assert.AreEqual( Partitioner.Partition.States.Unassigned, partition.State, "State of an unassigned partition has changed" );

				if( expectRemainder )
				{
					Assert.IsNotNull( remainder, "No remainder was generated" );
					Assert.AreEqual( offset, remainder.Offset, "Remainder has incorrect offset" );
					Assert.AreEqual( originalLength, partition.Length + remainder.Length, "Combined lengths do not match original" );
					Assert.AreEqual( Partitioner.Partition.States.Unassigned, remainder.State, "Remainder is not unassigned" );
				}
				else
					Assert.IsNull( remainder, "An unexpected remainder was returned" );
			}

			//--------------------------------------------------------------------------------------------------------------------
			//	Class	Partition
			//	Item	TruncateBefore()
			//	Tests	UnassignedNoRemainder
			//			TentativeNoRemainder
			//			UnassignedWithRemainder
			//			TentativeWithRemainder
			[TestMethod]
			public void TestPartition_Truncate_UnassignedNoRemainder()
			{
				TestPartition_Truncate_Helper( unassigned, 100 );
				return;
			}
			[TestMethod]
			public void TestPartition_Truncate_TentativeNoRemainder()
			{
				TestPartition_Truncate_Helper( tentative, 100 );
				return;
			}
			[TestMethod]
			public void TestPartition_Truncate_UnassignedWithRemainder()
			{
				TestPartition_Truncate_Helper( unassigned, 60 );
				return;
			}
			[TestMethod]
			public void TestPartition_Truncate_TentativeWithRemainder()
			{
				TestPartition_Truncate_Helper( tentative, 60 );
				return;
			}
		}
		#endregion

		#region Partition.Contains() tests
		[TestClass]
		public class Contains
		{
			[TestInitialize]
			public void TestInitialise()
			{
				PartitionTests.TestInitialise();
			}
			//--------------------------------------------------------------------------------------------------------------------
			//	Class	Partition
			//	Item	Contains()
			//	Tests	Whether a single offset is correctly identified as being contained in a partition.
			//
			[TestMethod]
			public void TestPartition_Contains_OffsetBeforeStart()
			{
				Assert.IsFalse( unassigned.Contains( 9 ) );
			}
			[TestMethod]
			public void TestPartition_Contains_OffsetAtStart()
			{
				Assert.IsTrue( unassigned.Contains( 10 ) );
			}
			[TestMethod]
			public void TestPartition_Contains_OffsetAtEnd()
			{
				Assert.IsTrue( unassigned.Contains( 99 ) );
			}
			[TestMethod]
			public void TestPartition_Contains_OffsetBeyondEnd()
			{
				Assert.IsFalse( unassigned.Contains( 100 ) );
			}
			//--------------------------------------------------------------------------------------------------------------------
			//	Class	Partition
			//	Item	ContainsOrNext()
			//	Tests	
			//
			[TestMethod]
			public void TestPartition_ContainsOrNext_OffsetBeforeStart()
			{
				Assert.IsFalse( unassigned.ContainsOrNext( 9 ) );
			}
			[TestMethod]
			public void TestPartition_ContainsOrNext_OffsetAtStart()
			{
				Assert.IsTrue( unassigned.ContainsOrNext( 10 ) );
			}
			[TestMethod]
			public void TestPartition_ContainsOrNext_OffsetAtEnd()
			{
				Assert.IsTrue( unassigned.ContainsOrNext( 99 ) );
			}
			[TestMethod]
			public void TestPartition_ContainsOrNext_OffsetAtNext()
			{
				Assert.IsTrue( unassigned.ContainsOrNext( 100 ) );
			}
			[TestMethod]
			public void TestPartition_ContainsOrNext_OffsetBeyondNext()
			{
				Assert.IsFalse( unassigned.ContainsOrNext( 101 ) );
			}
		}
		#endregion

		//--------------------------------------------------------------------------------------------------------------------
		//	Class	Partition
		//	Item	
		//	Tests	
		//
		//[TestMethod]
		//public void TestPartition_
		//--------------------------------------------------------------------------------------------------------------------
		//	Class	Partition
		//	Item	
		//	Tests	
		//
		//[TestMethod]
		//public void TestPartition_
		//--------------------------------------------------------------------------------------------------------------------
		//	Class	Partition
		//	Item	
		//	Tests	
		//
		//[TestMethod]
		//public void TestPartition_
		//--------------------------------------------------------------------------------------------------------------------
		//	Class	Partition
		//	Item	
		//	Tests	
		//
		//[TestMethod]
		//public void TestPartition_

		//--------------------------------------------------------------------------------------------------------------------
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
