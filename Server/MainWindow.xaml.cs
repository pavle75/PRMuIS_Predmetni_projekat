using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Server
{
    public partial class MainWindow : Window
    {
        private Socket udpSocket;
        private Socket tcpSocket;
        public List<Igrac> igraci = new List<Igrac>();
        private int brojIgraca;
        private int dimenzija;
        private int maxPromasaji;
        private bool serverPokrenut = false;
        private int tcpPort = 5001;
        private DispatcherTimer pollTimer;
        private bool krajIgre = false;

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

            await PokreniUdpServer(udpPort);
            await PokreniTcpServer();
        }

        private async Task PokreniUdpServer(int port)
        {
            try
            {
                udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                IPEndPoint localEP = new IPEndPoint(IPAddress.Any, port);
                udpSocket.Bind(localEP);

                Log($"UDP socket sluša na portu {port}");

                List<EndPoint> igracEndPoints = new List<EndPoint>();

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
                            Aktivan = (igracId == 1)
                        };

                        igraci.Add(noviIgrac);
                        igracEndPoints.Add(remoteEP);

                        Log($"Igrač {igracId} se prijavio sa {remoteEP}");

                        await Dispatcher.InvokeAsync(() =>
                        {
                            txtPrijavljeni.Text = $"Prijavljeni igrači: {igraci.Count}/{brojIgraca}";
                            lstIgraci.Items.Add($"Igrač {igracId} - {remoteEP}");
                        });
                    }
                }

                Log("Svi igrači prijavljeni! Šaljem TCP informacije...");

                for (int i = 0; i < igraci.Count; i++)
                {
                    string odgovor = $"TCP:{tcpPort},ID:{igraci[i].Id}";
                    byte[] data = Encoding.UTF8.GetBytes(odgovor);
                    await Task.Run(() => udpSocket.SendTo(data, igracEndPoints[i]));
                    Log($"TCP info poslata igraču {igraci[i].Id}");
                }

                udpSocket.Close();
                Log("UDP server zatvoren, pokrećem TCP server...");
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

                    // Primi ID od klijenta
                    byte[] buffer = new byte[64];
                    clientSocket.Blocking = true;
                    int bytesRead = clientSocket.Receive(buffer);
                    string idStr = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    int clientId = int.Parse(idStr);

                    // Dodeli socket pravom igraču
                    var igrac = igraci.FirstOrDefault(ig => ig.Id == clientId);
                    if (igrac != null)
                    {
                        igrac.TcpSocket = clientSocket;
                        clientSocket.Blocking = false;
                        Log($"Igrač {igrac.Id} povezan preko TCP-a");
                    }
                }

                await PosaljiParametreIgre();
                await PrimiPodmornice();
                await ObavjestiOPocetku();
                PokreniPolling();
            }
            catch (Exception ex)
            {
                Log($"Greška u TCP serveru: {ex.Message}");
            }
        }

        private async Task PosaljiParametreIgre()
        {
            string poruka = $"Broj igraca je {brojIgraca}! Velicina table je {dimenzija}x{dimenzija}, posaljite brojevne vrednosti koje predstavljaju polja vasih podmornica (1 - {dimenzija * dimenzija}). Ukupno dozvoljen broj promasaja: {maxPromasaji}";
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
                var parts = podmornice.Split('|');
                int id = int.Parse(parts[0]);
                Igrac trenutni = igraci.FirstOrDefault(i => i.Id == id);
                Console.WriteLine($"Primljene podmornice od igrača {trenutni.Id}");
                trenutni.Podmornice = parts[1].Split(',').Select(int.Parse).ToList();
                trenutni.Tabla = new int[dimenzija, dimenzija];

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

            Log("Potvrde poslate svim igračima");
        }

        private async Task ObavjestiOPocetku()
        {
            foreach (var igrac in igraci)
            {
                string poruka = igrac.Aktivan ? "TVOJ_POTEZ" : "CEKAJ_RED";
                byte[] data = Encoding.UTF8.GetBytes(poruka);

                await Task.Run(() =>
                {
                    try
                    {
                        igrac.TcpSocket.Blocking = true;
                        igrac.TcpSocket.Send(data);
                        igrac.TcpSocket.Blocking = false;
                    }
                    catch { }
                });
            }

            Log($"Igrač {igraci.First(i => i.Aktivan).Id} počinje!");
        }

        private void PokreniPolling()
        {
            Log("Pokretanje polling-a...");

            pollTimer = new DispatcherTimer();
            pollTimer.Interval = TimeSpan.FromMilliseconds(100);
            pollTimer.Tick += async (s, e) =>
            {
                foreach (var igrac in igraci)
                {
                    if (!krajIgre)
                    {
                        try
                        {
                            if (igrac.TcpSocket.Poll(0, SelectMode.SelectRead))
                            {
                                if (igrac.TcpSocket.Available > 0)
                                {
                                    await ObradiPoruku(igrac);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Greška polling: {ex.Message}");
                        }
                    }
                    else
                    {
                        pollTimer.Stop();
                    }
                }
            };
            pollTimer.Start();
            Log("Polling aktivan!");
        }

        private async Task ObradiPoruku(Igrac igrac)
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead = await Task.Run(() => igrac.TcpSocket.Receive(buffer));

                if (bytesRead == 0)
                {
                    Log($"Igrač {igrac.Id} se diskonektovao");
                    return;
                }

                string poruka = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (!igrac.Aktivan && poruka.StartsWith("GADJAJ:"))
                {
                    Log($"Igrač {igrac.Id} pokušao da igra van poteza!");
                    string greska = "NIJE_TVOJ_POTEZ";
                    byte[] data = Encoding.UTF8.GetBytes(greska);
                    await Task.Run(() => igrac.TcpSocket.Send(data));
                    return;
                }

                if (poruka.StartsWith("IZABERI:"))
                {
                    int protivnikId = int.Parse(poruka.Split(':')[1]);
                    var protivnik = igraci.FirstOrDefault(i => i.Id == protivnikId);

                    if (protivnik != null)
                    {
                        string tabla = GenerisiTablu(protivnik);
                        //await SlanjeTCPPoruke(igrac,$"TABLA:{tabla}");
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
                ponovnoPucanje = true;
            }
            else if (protivnik.Podmornice.Contains(polje))
            {
                protivnik.Tabla[red, kol] = 2;
                napadac.BrojPogodaka++;

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
                protivnik.Podmornice.Remove(polje);
                Log($"Igrač {napadac.Id} -> Igrač {protivnik.Id}: polje {polje}, {rezultat}");
                if (protivnik.Podmornice.Count == 0)
                {
                    await KrajIgre();
                    Log($"Igrač {napadac.Id} je pobedio igrača {protivnik.Id}");
                }
            }
            else
            {
                protivnik.Tabla[red, kol] = 1;
                napadac.BrojPromasaja++;
                rezultat = "PROMASIO";

                Log($"Igrač {napadac.Id} -> Igrač {protivnik.Id}: polje {polje}, {rezultat}");
                if (napadac.BrojPromasaja >= maxPromasaji)
                {
                    await KrajIgre();
                    Log($"Igrač {napadac.Id} je potrošio sve dozvoljene poteze");
                }
                else
                {
                    await SledeciIgracPotez(napadac);
                }

            }
            if (!krajIgre)
            {
                string odgovor = $"REZULTAT:{rezultat}:{(ponovnoPucanje ? "PONOVO" : "KRAJ")}";
                await SlanjeTCPPoruke(napadac, odgovor);
            }

        }

        private bool JePotopljena(Igrac igrac, int polje)
        {
            int red = (polje - 1) / dimenzija;
            int kol = (polje - 1) % dimenzija;

            try
            {
                if (red - 1 >= 0 && kol + 1 < dimenzija)
                {
                    if (igrac.Tabla[red - 1, kol] == 2 && igrac.Tabla[red, kol + 1] == 2)
                        return true;
                }

                if (red + 1 < dimenzija && kol + 1 < dimenzija)
                {
                    if (igrac.Tabla[red + 1, kol] == 2 && igrac.Tabla[red + 1, kol + 1] == 2)
                        return true;
                }

                if (red - 1 >= 0 && kol - 1 >= 0)
                {
                    if (igrac.Tabla[red, kol - 1] == 2 && igrac.Tabla[red - 1, kol - 1] == 2)
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log($"Greška pri provjeri potopljenosti: {ex.Message}");
            }

            return false;
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

        private async Task SlanjeTCPPoruke(Igrac igrac, string poruka)
        {
            byte[] data = Encoding.UTF8.GetBytes(poruka);
            await Task.Run(() =>
            {
                try
                {
                    igrac.TcpSocket.Blocking = true;
                    igrac.TcpSocket.Send(data);
                    igrac.TcpSocket.Blocking = false;
                }
                catch { }
            });
        }

        private async Task KrajIgre()
        {

            var sortIgraci = igraci.OrderByDescending(obj => obj.Podmornice.Count).ToList();
            Igrac pobednik = sortIgraci[0];

            if (pobednik.BrojPromasaja >= maxPromasaji)
            {
                pobednik = null;
                for (int i = 0; i < sortIgraci.Count; i++)
                {
                    if (sortIgraci[i].BrojPromasaja < maxPromasaji)
                    {
                        pobednik = sortIgraci[i];
                        break;
                    }
                }
                if (pobednik.BrojPromasaja >= maxPromasaji)
                {
                    sortIgraci = igraci.OrderByDescending(obj => obj.BrojPogodaka).ToList();
                    pobednik = sortIgraci[0];
                }
            }

            foreach (Igrac igrac in igraci)
            {
                string odgovor = $"KRAJ-IGRE: {((igrac.Id == pobednik.Id) ? "POBEDNIK" : "GUBITNIK")},{igrac.BrojPogodaka}";
                await SlanjeTCPPoruke(igrac, odgovor);
            }
            krajIgre = true;

            Log("Rang lista:");
            Log("-----------------------------------------------------------------------------");

            foreach (Igrac igrac in sortIgraci)
            {
                Log($"Igrac: {igrac.Id}, broj podmornica: {igrac.Podmornice.Count}, broj pogodaka: {igrac.BrojPogodaka}");
            }
            Log("-----------------------------------------------------------------------------");
            ZatvoriIgru();
        }

        private async Task SledeciIgracPotez(Igrac trenutniIgrac)
        {
            trenutniIgrac.Aktivan = false;
            int napadacIndex = igraci.IndexOf(trenutniIgrac);
            int sledeciIndex = (napadacIndex + 1) % igraci.Count;
            var sledeciIgrac = igraci[sledeciIndex];
            sledeciIgrac.Aktivan = true;
            await SlanjeTCPPoruke(sledeciIgrac, "TVOJ_POTEZ");
            Log($"Potez prebačen na igrača {sledeciIgrac.Id}");
        }

        private void ZatvoriIgru()
        {
            try
            {
                foreach (Igrac igrac in igraci)
                {
                    igrac.TcpSocket.Close();
                }
                tcpSocket.Close();
            }
            catch { }
        }

        private void BtnZaustavi_Click(object sender, RoutedEventArgs e)
        {
            ZatvoriIgru();
            this.Close();
        }
    }
}