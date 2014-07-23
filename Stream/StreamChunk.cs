using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO ;

namespace TripeSoft
{
	namespace IO
	{
		public class StreamChunk : Stream
		{
			private Stream	underlyingStream;
			private long	underlyingOffset;
			private long	chunkLength;

			public StreamChunk( Stream stream, long offset, long length )
			{
				if( !stream.CanSeek )
					throw new NotSupportedException( "Underlynig stream must be seekable" );
				if( offset+length > stream.Length )
					throw new ArgumentException( String.Format( "Chunk at {0} of length {1} exceeds length of underlying stream ({2})", offset, length, stream.Length ) );
				underlyingStream = stream;
				underlyingOffset = offset;
				chunkLength = length;
				Position = 0;
			}

			public StreamChunk( Stream stream, long length )
				: this( stream, stream.Position, length )
			{
			}

			public override long Position
			{
				get
				{
					long posn = underlyingStream.Position - underlyingOffset ;
					if( posn < 0 || posn >= chunkLength )
						throw new ArgumentException( "Underlying stream not positioned within chunk" );
					return posn;
				}
				set
				{
					Seek( value, SeekOrigin.Begin ) ;
				}
			}

			public override long Length
			{
				get{ return chunkLength; }
			}

			public override bool CanRead
			{
				get { return underlyingStream.CanRead; }
			}

			public override bool CanWrite
			{
				get { return underlyingStream.CanWrite; }
			}

			public override bool CanSeek
			{
				get { return true; }
			}

			public override int Read( byte[] buffer, int offset, int count )
			{
				if( !CanRead )
					throw new NotImplementedException( "Underlying stream does not support reading" );
				if( Position + count > chunkLength )
					throw new NotSupportedException();
				return underlyingStream.Read( buffer, offset, count );
			}

			public override void Write( byte[] buffer, int offset, int count )
			{
				if( !CanWrite )
					throw new NotImplementedException( "Underlying stream does not support writing" );
				if( Position + count > chunkLength )
					throw new NotSupportedException();
				underlyingStream.Write( buffer, offset, count );
			}

			public override long Seek( long offset, SeekOrigin origin )
			{
				switch( origin )
				{
					case SeekOrigin.Begin:
						if( offset < 0 || offset >= chunkLength )
							throw new ArgumentOutOfRangeException( String.Format( "Seek {0} from beginning is outside chunk boundary [{1}]", offset, Length ) );
						return underlyingStream.Seek( underlyingOffset + offset, origin );

					case SeekOrigin.Current:
						long posn = Position;
						if( posn + offset < 0 || posn + offset >= Length )
							throw new ArgumentOutOfRangeException( String.Format( "Seek {0} from current is outside chunk boundary [{1}]", offset, Length ) );
						return underlyingStream.Seek( offset, origin );

					case SeekOrigin.End:
						if( chunkLength + offset < 0 || chunkLength + offset >= chunkLength )
							throw new ArgumentOutOfRangeException( String.Format( "Seek {0} from end is outside chunk boundary [{1}]", offset, Length ) );
						return underlyingStream.Seek( underlyingOffset + chunkLength - 1 + offset, origin );

					default:
						throw new ArgumentException();
				}
			}

			public override void Flush()
			{
				throw new NotImplementedException();
			}

			public override void SetLength( long value )
			{
				throw new NotImplementedException();
			}
		}
	}
}
