using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace MemMapTest
{
	class Extent
	{
		private List<Extent> subExtents ;

		public enum ExtentStatus
		{
			Unassigned,
			Tentative,
			Assigned,
		}

		public long			Length	{ get ; private set ; }
		public long			Offset	{ get ; private set ; }
		public ExtentStatus	Status	{ get ; private set ; }

		public Extent( long offset, long length )
		{
			Offset = offset ;
			Length = length ;
			Status = ExtentStatus.Assigned ;
			subExtents = null ;
		}
		public long EndPos
		{
			get { return Offset + Length ; }
		}
		public bool IsOuterWithin( long outerOffset )
		{
			return ( outerOffset < Offset ) || ( outerOffset > EndPos ) ;
		}
		public bool IsInnerWithin( long innerOffset )
		{
			return ( innerOffset < 0 ) || ( innerOffset > Length ) ;
		}
		public long InnerOffset( long outerOffset )
		{
			if( !IsOuterWithin( outerOffset ) )
				throw new ArgumentOutOfRangeException( "outerOffset", outerOffset, "Not within extent " + this ) ;
			return outerOffset - Offset ;
		}
		public long OuterOffset( long innerOffset )
		{
			if( !IsInnerWithin( innerOffset ) )
				throw new ArgumentOutOfRangeException( "innerOffset", innerOffset, "Not within extent " + this ) ;
			return Offset + innerOffset ;
		}
		public override string ToString()
		{
			return String.Format( "[ofs={0},len={1}]", Offset, Length ) ;
		}
	}

	class StreamExtent : Stream
	{
		private Extent	extent ;
		private Stream	stream ;

		//	Constructors
		//
		public StreamExtent( Stream stream )
			: this( stream, 0, stream.Length )
		{
		}
		public StreamExtent( Stream stream, long length )
			: this( stream, stream.Position, length )
		{
		}
		public StreamExtent( Stream stream, long offset, long length )
		{
			if( !stream.CanSeek )
				throw new NotSupportedException( "Underlynig stream must be seekable" ) ;
			if( offset+length > stream.Length )
				throw new ArgumentOutOfRangeException( String.Format( "Chunk at {0} of length {1} exceeds length of underlying stream ({2})", offset, length, stream.Length ) );
			this.stream = stream ;
			this.extent = new Extent( offset, length ) ;
			Position = 0 ;
		}

		//	Overrides from 'Stream'
		//
		public override bool CanRead
		{
			get { return stream.CanRead ; }
		}
		public override bool CanSeek
		{
			get { return stream.CanSeek ; }
		}
		public override bool CanTimeout
		{
			get { return stream.CanTimeout ; }
		}
		public override bool CanWrite
		{
			get { return stream.CanWrite ; }
		}
		public override long Length
		{
			get { return extent.Length ; }
		}
		public override long Position
		{
			get
			{
				return extent.InnerOffset( stream.Position ) ;
			}
			set
			{
				Seek( value, SeekOrigin.Begin ) ;
			}
		}
		public override long Seek( long offset, SeekOrigin origin )
		{
			switch( origin )
			{
				case SeekOrigin.Begin:
					return extent.InnerOffset( stream.Seek( extent.OuterOffset( offset ), SeekOrigin.Begin ) ) ;

				case SeekOrigin.Current:
					return Seek( Position + offset, SeekOrigin.Begin ) ;

				case SeekOrigin.End:
					return Seek( extent.Length + offset, SeekOrigin.Begin ) ;

				default:
					throw new ArgumentException();
			}
		}
		public override int Read( byte[] buffer, int offset, int count )
		{
			ThrowIfNoSpace( count ) ;
			return stream.Read( buffer, offset, count ) ;
		}
		public override int ReadByte()
		{
			ThrowIfNoSpace( 1 ) ;
			return stream.ReadByte() ;
		}
		public override void Write( byte[] buffer, int offset, int count )
		{
			ThrowIfNoSpace( count ) ;
			stream.Write( buffer, offset, count ) ;
		}
		public override void WriteByte( byte value )
		{
			ThrowIfNoSpace( 1 ) ;
			stream.WriteByte( value );
		}
		public override void Flush()
		{
			stream.Flush() ;
		}

		//	Not implemented (won't be!)
		//
		public override void SetLength( long value )
		{
			throw new NotImplementedException();
		}

		//	Helper functions
		//
		public void ThrowIfNoSpace( long count )
		{
			if( !extent.IsInnerWithin( Position + count ) )
				throw new ArgumentOutOfRangeException( "count", count, "Extends beyond end of extent " + extent ) ;
		}
	}

	class Program
	{
		static void Main( string[] args )
		{
		}
	}
}
