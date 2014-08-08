using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TripeSoft.Utils
{
	public class Partitioner
	{
		public class Partition
		{
			public enum		States	{ Unassigned, Tentative, Assigned } ;
			public long		Offset	{ get; private set; }
			public long		Length	{ get; private set; }
			public States	State	{ get; private set; }

			//TODO: Members/properties to hold external type and/or object?

			/// <summary>
			/// Create a Partition class of the given size and type.
			/// </summary>
			/// <param name="offset">Offset (within containing space) of the new partition.</param>
			/// <param name="length">Length of the new partition.</param>
			public Partition( long offset, long length, States state )
			{
				//TODO: Prevent silly values
				Offset	= offset;
				Length	= length;
				State	= state;
			}

			public override string ToString()
			{
				return String.Format( "[{0},{1},{2}]", Offset, Length, State );
			}

			public bool IsUnassigned
			{
				get { return State == States.Unassigned; }
			}
			public bool IsTruncatable
			{
				get { return State != States.Assigned; }
			}
			public bool IsTentative
			{
				get { return State == States.Tentative; }
			}
			public bool IsAssigned
			{
				get { return State == States.Assigned; }
			}

			/// <summary>
			/// Marks (an unassigned) partition as in use.
			/// </summary>
			/// <exception cref="InvalidOperationException">The partition space has already been assigned.</exception>
			public void MarkAssigned()
			{
				if( IsAssigned )
					throw new InvalidOperationException( ExMsg.PartitionAlreadyAssigned );
				State = States.Assigned;
			}

			/// <summary>
			/// 
			/// </summary>
			/// <param name="offset"></param>
			/// <returns></returns>
			public Partition TruncateBefore( long offset )
			{
				Partition remainder = null;
				if( !IsTruncatable )
					throw new InvalidOperationException( ExMsg.PartitionNotTruncatable );
				if( offset < Offset || offset > NextOffset )
					throw new ArgumentOutOfRangeException( "offset", offset, ExMsg.TruncationOutsidePartition );
				if( offset == Offset )
					throw new ArgumentOutOfRangeException( "offset", offset, ExMsg.TruncationWouldDelete );
				if( offset < NextOffset )
					remainder = new Partition( offset, Remainder( offset ), States.Unassigned );
				Length = offset - Offset;
				if( State == States.Tentative )
					State = States.Assigned;
				return remainder;
			}

			/// <summary>
			/// The offset of the location just beyond the end of the parition.
			/// </summary>
			public long NextOffset
			{
				get {
					return Offset + Length;
				}
			}

			/// <summary>
			/// Determines the length of the remainder of the current partition beyond a given offset.
			/// </summary>
			/// <param name="offset">Offset (from parent).</param>
			/// <returns>The length remaining.</returns>
			/// <exception cref="ArgumentOutOfRangeException">The given offset does not lie within the partition.</exception>
			public long Remainder( long offset )
			{
				if( !Contains( offset ) )
					throw new ArgumentOutOfRangeException( "offset", offset, "Offset not within partition" ) ;
				return NextOffset - offset;
			}

			/// <summary>
			/// Determines if a given offset is included in the partition.
			/// </summary>
			/// <param name="offset">Offset to test.</param>
			/// <returns>True if the offset is contained in the partition.</returns>
			public bool Contains( long offset )
			{
				return ( offset >= Offset ) && ( offset < NextOffset );
			}

			/// <summary>
			/// Determines if the area specified by the offset and length lies wholly within the current partition.
			/// </summary>
			/// <param name="offset">Offset (from parent)</param>
			/// <param name="length">Length</param>
			/// <returns>'true' if the specified region falls entirely within the current partition.</returns>
			public bool Contains( long offset, long length )
			{
				return Contains( offset ) && ( offset + length ) <= NextOffset;
			}
			/// <summary>
			/// Determines if the given partition lies wholly within the current partition.
			/// </summary>
			/// <param name="partition">Partition to check.</param>
			/// <returns>'true' if the specified region falls entirely within the current partition.</returns>
			public bool Contains( Partition partition )
			{
				return Contains( partition.Offset, partition.Length );
			}

			/// <summary>
			/// Determines if the given offset lies IN the partition OR immediately after it.
			/// </summary>
			/// <param name="offset">Offset to test.</param>
			/// <returns>True if the offset lies in or immediately after the partition.</returns>
			public bool ContainsOrNext( long offset )
			{
				return ( offset >= Offset ) && ( offset <= NextOffset );
			}
			/// <summary>
			/// Determines if the area specified by the offset and length exactly matches the current partition.
			/// </summary>
			/// <param name="offset">Offset (from parent)</param>
			/// <param name="length">Length</param>
			/// <returns>'true' if the specified region exactly matches the current partition.</returns>
			public bool Matches( long offset, long length )
			{
				return offset == Offset
					&& length == Length;
			}
			/// <summary>
			/// Determines if the given partition exactly matches the current partition (ignoring type).
			/// </summary>
			/// <param name="offset">Offset (from parent)</param>
			/// <param name="length">Length</param>
			/// <returns>'true' if the specified region exactly matches the current partition.</returns>
			public bool Matches( Partition partition )
			{
				return Matches( partition.Offset, partition.Length );
			}
		}

		private List<Partition> partitions;
		public long Length { get; private set; }

		public static class ExMsg
		{
			public const string NotInParentPartition		= "Outside partitioned space";
			public const string PartitionAlreadyAssigned	= "Partition has already been assigned";
			public const string PartitionNotTruncatable		= "Partition is not truncatable";
			public const string TruncationOutsidePartition	= "Truncation point is outside partiton";
			public const string TruncationWouldDelete		= "Truncation would delete partition";
		}
		public Partitioner( long length )
		{
			Length = length;
			partitions = new List<Partition>();
			partitions.Add( new Partition( 0, Length, Partition.States.Unassigned ) );
		}

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendFormat( "Length:{0} Partitions:{1}\n", Length, partitions.Count );
			foreach( Partition partition in partitions )
			{
				sb.Append( partition.ToString() + "\n" );
			}
			return sb.ToString();
		}
		public Partition ContainingPartition( long offset )
		{
			if( offset < 0 || offset >= Length )
				throw new ArgumentOutOfRangeException( "offset", offset, ExMsg.NotInParentPartition );
			foreach( Partition partition in partitions )
			{
				if( offset >= partition.Offset && offset < partition.Offset + partition.Length )
					return partition;
			}
			throw new ArgumentOutOfRangeException( "offset", offset, "Not found within any partition" );
		}

		/// <summary>
		/// Create a new partition of size 'length' starting at 'offset'.  The area must currently be unassigned.
		/// </summary>
		/// <param name="offset">Offset of the beginning of the partition.</param>
		/// <param name="length">Length of the partition.</param>
		/// <returns>The new Partition</returns>
		/// <exception cref="ArgumentOutOfRangeException"
		public Partition CreatePartition( long offset, long length )
		{
			Partition existing  = ContainingPartition( offset );
			Partition partition = null;
			Partition remainder = null;

			//Check:
			//	The start of the new partition is in unassigned space, and that it does not cross into another
			//	partition (it is assumed that there will not be adjacent blocks of unassigned space).  If the
			//	new partition leaves unassigned space after it, create another partition to represent it.
			//


			//	Check that the start of the new partition lies in an unassigned partition and that the new partition
			//	does not extend into the next one (it is assumed that there will NOT be adjacent unassigned partitions).
			//
			if( !existing.IsUnassigned )
				throw new ArgumentOutOfRangeException( "offset", offset, "Already assigned to a partition" );
			if( !existing.Contains( offset, length ) )
				throw new ArgumentOutOfRangeException( "length", length, "Crosses a partition boundary" );

			//	If the new partition EXACTLY matches the unassigned one, change its type and return.
			//
			if( existing.Matches( offset, length ) )
			{
				existing.MarkAssigned();
				return existing ;
			}

			//	If the new partition is at the START of the unassigned space, change the type, create a
			//	partition for the remainder, truncate the existing partition and add the remainder to the list.
			//
			if( existing.Offset == offset )
			{
				remainder = existing.TruncateBefore( offset + length );
				existing.MarkAssigned();
				partitions.Add( remainder ) ;
				return existing ;
			}

			//	Otherwise, the new partition DIVIDES the existing unassigned space: create partitions for the
			//	new space (and the remainder, if any).  Truncate the existing partition and the new/remainder.
			//
			partition = existing.TruncateBefore( offset );
			if( partition.Length > length )
				remainder = partition.TruncateBefore( offset + length );
			partitions.Add( partition );
			if( remainder != null )
				partitions.Add( remainder );
			return partition;
		}
	}
}
