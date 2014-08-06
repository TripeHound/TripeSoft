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
		}

		private List<Partition> partitions;

		public Partitioner( long maxLength )
		{
			partitions = new List<Partition>();
			partitions.Add( new Partition( 0, maxLength, Partition.PartType.Unassigned ) );
		}

		public Partition ContainingPartition( long offset )
		{
			foreach( Partition partition in partitions )
			{
				if( offset >= partition.Offset && offset < partition.Offset + partition.Length )
					return partition;
			}
			return null;
		}
	}
}
