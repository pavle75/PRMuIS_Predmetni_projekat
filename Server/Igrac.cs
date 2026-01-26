using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class Igrac
    {
        public int Id { get; set; }
        public int BrojPromasaja { get; set; }
        public int BrojPogodaka { get; set; }
        public List<int> Podmornice { get; set; } = new List<int>();
        public int[,] Tabla { get; set; }
        public Socket TcpSocket { get; set; }
        public IPEndPoint UdpEndPoint { get; set; }
        public bool Aktivan { get; set; } = true;
    }
}
