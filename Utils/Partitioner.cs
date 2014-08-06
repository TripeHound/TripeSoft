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
			public enum PartType
			{
				Unassigned,
				FixedSize,
				Truncatable
			}
			public long Offset { get; private set; }
			public long Length { get; private set; }
			public PartType Type { get; private set; }

			public Partition( long offset, long length, PartType type )
			{
				Offset = offset;
				Length = length;
				Type   = type;
			}
			public long NextOffset
			{
				get {
					return Offset + Length;
				}
			}
			public long Remainder( 
			{
				get {
					return 
				}
			}
		}

		private List<Partition> partitions;
		public long Length { get; private set; }

		public Partitioner( long length )
		{
			Length = length;
			partitions = new List<Partition>();
			partitions.Add( new Partition( 0, Length, Partition.PartType.Unassigned ) );
		}

		public Partition ContainingPartition( long offset )
		{
			if( offset < 0 || offset >= Length )
				throw new ArgumentOutOfRangeException( "offset", offset, "Outside partitioned space" );
			foreach( Partition partition in partitions )
			{
				if( offset >= partition.Offset && offset < partition.Offset + partition.Length )
					return partition;
			}
			throw new ArgumentOutOfRangeException( "offset", offset, "Not found within any partition" );
		}

		public Partition CreatePartition( long offset, long length )
		{
			Partition existing = ContainingPartition( offset );
			Partition newPart  = new Partition( offset, length, Partition.PartType.FixedSize ) ;
			if( existing.Type == Partition.PartType.FixedSize )
				throw new ArgumentOutOfRangeException( "offset", offset, "Already assigned to a partition" );
			if( newPart.NextOffset > existing.NextOffset )
				throw new ArgumentOutOfRangeException( "length", length, "Crosses a partition boundary" );
			if( newPart.NextOffset < existing.NextOffset )
				partitions.Add( new Partition( newPart.NextOffset, existing.Remainder( newPart.NextOffset ), existing.Type ) );
			existing.Length = offset - existing.Offset;
			//TODO:
			return existing;
		}
	}
}
