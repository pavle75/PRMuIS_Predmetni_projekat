using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Server
{
    public partial class MainWindow : Window
    {
        private Socket udpSocket;
        private Socket tcpSocket;
        private List<Igrac> igraci = new List<Igrac>();
        private int brojIgraca;
        private int dimenzija;
        private int maxPromasaji;
        private bool serverPokrenut = false;
        private int tcpPort = 5001;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnPokreni_Click(object sender, RoutedEventArgs e)
        {
            if (serverPokrenut) return;

            if (!int.TryParse(txtBrojIgraca.Text, out brojIgraca) || brojIgraca < 2)
            {
                MessageBox.Show("Unesite validan broj igrača (minimum 2)");
                return;
            }

            if (!int.TryParse(txtDimenzija.Text, out dimenzija) || dimenzija < 5)
            {
                MessageBox.Show("Unesite validnu dimenziju table (minimum 5)");
                return;
            }

            if (!int.TryParse(txtPromasaji.Text, out maxPromasaji) || maxPromasaji < 1)
            {
                MessageBox.Show("Unesite validan broj promašaja");
                return;
            }

            if (!int.TryParse(txtUdpPort.Text, out int udpPort))
            {
                MessageBox.Show("Unesite validan UDP port");
                return;
            }

            serverPokrenut = true;
            btnPokreni.IsEnabled = false;
            btnZaustavi.IsEnabled = true;

            Log("Server pokrenut...");
            txtStatus.Text = "Server aktivan";

            PokreniUdpServer(udpPort);
            PokreniTcpServer();
        }

        private async Task PokreniUdpServer(int port)
        {
            try
            {
                udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                IPEndPoint localEP = new IPEndPoint(IPAddress.Any, port);
                udpSocket.Bind(localEP);

                Log($"UDP socket sluša na portu {port}");

                while (igraci.Count < brojIgraca)
                {
                    byte[] buffer = new byte[1024];
                    EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                    int bytesReceived = await Task.Run(() => udpSocket.ReceiveFrom(buffer, ref remoteEP));
                    string poruka = Encoding.UTF8.GetString(buffer, 0, bytesReceived);

                    if (poruka == "PRIJAVA")
                    {
                        int igracId = igraci.Count + 1;
                        Igrac noviIgrac = new Igrac
                        {
                            Id = igracId,
                            UdpEndPoint = (IPEndPoint)remoteEP
                        };

                        igraci.Add(noviIgrac);
                        Log($"Igrač {igracId} se prijavio sa {remoteEP}");

                        string odgovor = $"TCP:{tcpPort},ID:{igracId}";
                        byte[] data = Encoding.UTF8.GetBytes(odgovor);
                        await Task.Run(() => udpSocket.SendTo(data, remoteEP));

                        txtPrijavljeni.Text = $"Prijavljeni igrači: {igraci.Count}/{brojIgraca}";
                        lstIgraci.Items.Add($"Igrač {igracId} - {remoteEP}");
                    }
                }

                udpSocket.Close();
                Log("Svi igrači prijavljeni, pokrećem TCP server...");
            }
            catch (Exception ex)
            {
                Log($"Greška u UDP serveru: {ex.Message}");
            }
        }

        private async Task PokreniTcpServer()
        {
            try
            {
                tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint localEP = new IPEndPoint(IPAddress.Any, tcpPort);
                tcpSocket.Bind(localEP);
                tcpSocket.Listen(10);

                Log($"TCP socket sluša na portu {tcpPort}");

                for (int i = 0; i < brojIgraca; i++)
                {
                    Socket clientSocket = await Task.Run(() => tcpSocket.Accept());
                    igraci[i].TcpSocket = clientSocket;

                    clientSocket.Blocking = false;

                    Log($"Igrač {igraci[i].Id} povezan preko TCP-a");
                }

                await PosaljiParametreIgre();
                await PrimiPodmornice();
                await PokreniPolling();
            }
            catch (Exception ex)
            {
                Log($"Greška u TCP serveru: {ex.Message}");
            }
        }

        private async Task PosaljiParametreIgre()
        {
            string poruka = $"Velicina table je {dimenzija}x{dimenzija}, posaljite brojevne vrednosti koje predstavljaju polja vasih podmornica (1 - {dimenzija * dimenzija}). Ukupno dozvoljen broj promasaja: {maxPromasaji}";
            byte[] data = Encoding.UTF8.GetBytes(poruka);

            foreach (var igrac in igraci)
            {
                await Task.Run(() =>
                {
                    igrac.TcpSocket.Blocking = true;
                    igrac.TcpSocket.Send(data);
                    igrac.TcpSocket.Blocking = false;
                });
            }

            Log("Parametri igre poslati svim igračima");
        }

        private async Task PrimiPodmornice()
        {
            foreach (var igrac in igraci)
            {
                byte[] buffer = new byte[1024];

                int bytesRead = await Task.Run(() =>
                {
                    igrac.TcpSocket.Blocking = true;
                    int bytes = igrac.TcpSocket.Receive(buffer);
                    igrac.TcpSocket.Blocking = false;
                    return bytes;
                });

                string podmornice = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                igrac.Podmornice = podmornice.Split(',').Select(int.Parse).ToList();

                igrac.Tabla = new int[dimenzija, dimenzija];

                Log($"Igrač {igrac.Id} postavio podmornice: {podmornice}");
            }

            foreach (var igrac in igraci)
            {
                string potvrda = "PODMORNICE_PRIMLJENE";
                byte[] data = Encoding.UTF8.GetBytes(potvrda);

                await Task.Run(() =>
                {
                    igrac.TcpSocket.Blocking = true;
                    igrac.TcpSocket.Send(data);
                    igrac.TcpSocket.Blocking = false;
                });
            }
        }

        private async Task PokreniPolling()
        {
            while (serverPokrenut)
            {
                foreach (var igrac in igraci.Where(i => i.Aktivan))
                {
                    if(igrac.TcpSocket.Poll(1000 * 1000, SelectMode.SelectRead))
                        ObradiPoruku(igrac);
                }
                await Task.Delay(100);
            }
        }

        private async Task ObradiPoruku(Igrac igrac)    // ovo trebas pozvati kad implemetiras polling
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead = await Task.Run(() => igrac.TcpSocket.Receive(buffer));

                if (bytesRead == 0)
                {
                    Log($"Igrač {igrac.Id} se diskonektovao");
                    igrac.Aktivan = false;
                    return;
                }

                string poruka = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (poruka.StartsWith("IZABERI:"))
                {
                    int protivnikId = int.Parse(poruka.Split(':')[1]);
                    var protivnik = igraci.FirstOrDefault(i => i.Id == protivnikId);

                    if (protivnik != null)
                    {
                        string tabla = GenerisiTablu(protivnik);
                        byte[] data = Encoding.UTF8.GetBytes($"TABLA:{tabla}");
                        await Task.Run(() => igrac.TcpSocket.Send(data));
                    }
                }
                else if (poruka.StartsWith("GADJAJ:"))
                {
                    string[] delovi = poruka.Split(':');
                    int protivnikId = int.Parse(delovi[1]);
                    int polje = int.Parse(delovi[2]);

                    await ObradiGadjanje(igrac, protivnikId, polje);
                }
            }
            catch (SocketException se)
            {
                if (se.SocketErrorCode != SocketError.WouldBlock)
                {
                    Log($"Socket greška pri obradi poruke za igrača {igrac.Id}: {se.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"Greška pri obradi poruke za igrača {igrac.Id}: {ex.Message}");
            }
        }

        private async Task ObradiGadjanje(Igrac napadac, int protivnikId, int polje)
        {
            var protivnik = igraci.FirstOrDefault(i => i.Id == protivnikId);
            if (protivnik == null) return;

            int red = (polje - 1) / dimenzija;
            int kol = (polje - 1) % dimenzija;

            string rezultat;
            bool ponovnoPucanje = false;

            if (protivnik.Tabla[red, kol] != 0)
            {
                rezultat = "VEC_GADJANO";
            }
            else if (protivnik.Podmornice.Contains(polje))
            {
                protivnik.Tabla[red, kol] = 2;

                napadac.BrojPogodaka++;

                //protivnik.Podmornice.Remove(polje);

                if (JePotopljena(protivnik, polje))
                {
                    rezultat = "POTOPIO";
                    ponovnoPucanje = true;
                }
                else
                {
                    rezultat = "POGODIO";
                    ponovnoPucanje = true;
                }

                Log($"Igrač {napadac.Id} -> Igrač {protivnik.Id}: polje {polje}, {rezultat}");
            }
            else
            {
                protivnik.Tabla[red, kol] = 1;

                napadac.BrojPromasaja++;

                rezultat = "PROMASIO";

                Log($"Igrač {napadac.Id} -> Igrač {protivnik.Id}: polje {polje}, {rezultat}");
            }

            string odgovor = $"REZULTAT:{rezultat}:{(ponovnoPucanje ? "PONOVO" : "KRAJ")}";
            byte[] data = Encoding.UTF8.GetBytes(odgovor);
            await Task.Run(() => napadac.TcpSocket.Send(data));
        }

        private bool JePotopljena(Igrac igrac, int polje)
        {
            return true;
        }

        private string GenerisiTablu(Igrac igrac)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < dimenzija; i++)
            {
                for (int j = 0; j < dimenzija; j++)
                {
                    if (igrac.Tabla[i, j] == 0)
                        sb.Append("0");
                    else if (igrac.Tabla[i, j] == 1)
                        sb.Append("#");
                    else if (igrac.Tabla[i, j] == 2)
                        sb.Append("X");

                    if (j < dimenzija - 1)
                        sb.Append(",");
                }
                if (i < dimenzija - 1)
                    sb.Append(";");
            }

            return sb.ToString();
        }

        private void Log(string poruka)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {poruka}\n");
                txtLog.ScrollToEnd();
            });
        }

        private void BtnZaustavi_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}