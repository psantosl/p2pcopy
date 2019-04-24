using System;
using System.Runtime.CompilerServices;

using PseudoTcp;
using gboolean = System.Boolean;
using gint = System.Int32;
using guint32 = System.UInt32;
using size_t = System.UInt32;

namespace p2pcopy
{
	/** Helper class to synchronize relevant methods in PseudoTcpSocket */
	public static class SyncPseudoTcpSocket
	{
		[MethodImpl(MethodImplOptions.Synchronized)]
		public static gint Send(PseudoTcpSocket sock, byte[] buffer, guint32 len)
		{
			return sock.Send (buffer, len);
		}

		[MethodImpl(MethodImplOptions.Synchronized)]
		public static gint Recv(PseudoTcpSocket sock, byte[] buffer, size_t len)
		{
			return sock.Recv (buffer, len);
		}

		[MethodImpl(MethodImplOptions.Synchronized)]
		public static gboolean NotifyPacket(PseudoTcpSocket sock, byte[] buffer, guint32 len)
		{
			return sock.NotifyPacket (buffer, len);
		}

		[MethodImpl(MethodImplOptions.Synchronized)]
		public static void NotifyClock(PseudoTcpSocket sock)
		{
			sock.NotifyClock ();
		}			
	}
}
