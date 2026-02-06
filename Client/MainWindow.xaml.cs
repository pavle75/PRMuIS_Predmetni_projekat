using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Client
{
    public partial class MainWindow : Window
    {
        private Socket udpSocket;
        private Socket tcpSocket;
        private int mojId;
        private int dimenzija;
        private int maxPromasaji;
        private List<int> odabranaPolja = new List<int>();
        private List<int> svaPoljaPodmornica = new List<int>();
        private List<Button> dugmadPodmornice = new List<Button>();
        private List<Button> dugmadTabla = new List<Button>();
        private int trenutniProtivnik = -1;
        private DispatcherTimer pollTimer;
        private bool mojRed = true;
        private int brojPolja = 3;
        private bool igraTraje = true;
        private int brIgraca = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnPrijavi_Click(object sender, RoutedEventArgs e)
        {
            if (!IPAddress.TryParse(txtServerIp.Text, out IPAddress serverIp))
            {
                MessageBox.Show("Unesite validnu IP adresu servera");
                return;
            }

            if (!int.TryParse(txtUdpPort.Text, out int udpPort))
            {
                MessageBox.Show("Unesite validan UDP port");
                return;
            }

            try
            {
                btnPrijavi.IsEnabled = false;
                Log("Slanje prijave serveru...");

                udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                IPEndPoint serverEndPoint = new IPEndPoint(serverIp, udpPort);

                byte[] data = Encoding.UTF8.GetBytes("PRIJAVA");
                await Task.Run(() => udpSocket.SendTo(data, serverEndPoint));
                Log("Prijava poslata, čekam odgovor...");

                byte[] buffer = new byte[1024];
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                int bytesReceived = await Task.Run(() => udpSocket.ReceiveFrom(buffer, ref remoteEP));
                string odgovor = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
                Log($"Primljen odgovor: {odgovor}");

                if (odgovor.StartsWith("TCP:"))
                {
                    var delovi = odgovor.Split(',');
                    int tcpPort = int.Parse(delovi[0].Split(':')[1]);
                    mojId = int.Parse(delovi[1].Split(':')[1]);

                    Log($"Primljena TCP informacija - Port: {tcpPort}, Moj ID: {mojId}");

                    await PoveziSeNaTcp(serverIp, tcpPort);
                }

                udpSocket.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri prijavi: {ex.Message}");
                Log($"GREŠKA: {ex.Message}");
                btnPrijavi.IsEnabled = true;
            }
        }

        private async Task PoveziSeNaTcp(IPAddress serverIp, int port)
        {
            try
            {
                tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint serverEndPoint = new IPEndPoint(serverIp, port);

                await Task.Run(() => tcpSocket.Connect(serverEndPoint));

                tcpSocket.Blocking = false;

                Log("Povezan sa TCP serverom");

                byte[] buffer = new byte[1024];
                int bytesRead = await Task.Run(() =>
                {
                    tcpSocket.Blocking = true;
                    int bytes = tcpSocket.Receive(buffer);
                    tcpSocket.Blocking = false;
                    return bytes;
                });

                string poruka = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                ParsirajParametre(poruka);

                grpStatus.Visibility = Visibility.Visible;
                txtStatus.Text = "Status: Povezan";
                txtMojId.Text = $"Moj ID: {mojId}";
                txtInfo.Text = poruka;

                grpPodmornice.Visibility = Visibility.Visible;
                KreirajGridPodmornice();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri TCP povezivanju: {ex.Message}");
                btnPrijavi.IsEnabled = true;
            }
        }

        private void ParsirajParametre(string poruka)
        {
            var delovi = poruka.Split(new[] { ' ', ',', ':' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var deo in delovi)
            {
                if (deo.Contains("x"))
                {
                    dimenzija = int.Parse(deo.Split('x')[0]);
                }
                if (deo.Contains("!"))
                {
                    brIgraca = int.Parse(deo.Trim('!'));
                }
            }

            var indexPromasaja = poruka.LastIndexOf(':');
            if (indexPromasaja > 0)
            {
                string brojStr = poruka.Substring(indexPromasaja + 1).Trim();
                maxPromasaji = int.Parse(brojStr);
            }

            Log($"Dimenzija table: {dimenzija}x{dimenzija}, Max promašaji: {maxPromasaji}");
        }

        private void KreirajGridPodmornice()
        {
            gridPodmornice.Children.Clear();
            dugmadPodmornice.Clear();
            gridPodmornice.Rows = dimenzija;
            gridPodmornice.Columns = dimenzija;

            for (int i = 0; i < dimenzija * dimenzija; i++)
            {
                Button btn = new Button
                {
                    Content = (i + 1).ToString(),
                    Margin = new Thickness(2),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Tag = i + 1
                };

                btn.Click += BtnPolje_Click;
                dugmadPodmornice.Add(btn);
                gridPodmornice.Children.Add(btn);
            }
        }

        private void BtnPolje_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            int polje = (int)btn.Tag;

            if (odabranaPolja.Contains(polje))
            {
                var poljaZaUklanjanje = DobijPoljaLPodmornice(polje);

                foreach (var p in poljaZaUklanjanje)
                {
                    svaPoljaPodmornica.Remove(p);
                    var dugme = dugmadPodmornice.FirstOrDefault(d => (int)d.Tag == p);
                    if (dugme != null)
                    {
                        dugme.Background = SystemColors.ControlBrush;
                    }
                }

                odabranaPolja.Remove(polje);
            }
            else
            {
                if (odabranaPolja.Count < brojPolja)
                {
                    if (MozePostavitiLPodmornicu(polje))
                    {
                        odabranaPolja.Add(polje);

                        var poljaLPodmornice = DobijPoljaLPodmornice(polje);

                        foreach (var p in poljaLPodmornice)
                        {
                            svaPoljaPodmornica.Add(p);
                            var dugme = dugmadPodmornice.FirstOrDefault(d => (int)d.Tag == p);
                            if (dugme != null)
                            {
                                if (p == polje)
                                    dugme.Background = Brushes.DarkBlue;
                                else
                                    dugme.Background = Brushes.LightBlue;
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Ne možete postaviti podmornicu ovde!");
                    }
                }
            }

            odabranaPolja.Sort();
            txtOdabranaPolja.Text = string.Join(", ", odabranaPolja);
            btnPosaljiPodmornice.IsEnabled = odabranaPolja.Count == brojPolja;
        }

        private List<int> DobijPoljaLPodmornice(int gornjePolje)
        {
            List<int> polja = new List<int> { gornjePolje };

            int donjeLeviPolje = gornjePolje + dimenzija;
            int donjeDesnoPolje = donjeLeviPolje + 1;

            polja.Add(donjeLeviPolje);
            polja.Add(donjeDesnoPolje);

            return polja;
        }

        private bool MozePostavitiLPodmornicu(int gornjePolje)
        {
            int red = (gornjePolje - 1) / dimenzija;
            int kol = (gornjePolje - 1) % dimenzija;

            if (red + 1 >= dimenzija) return false;
            if (kol + 1 >= dimenzija) return false;

            var poljaLPodmornice = DobijPoljaLPodmornice(gornjePolje);

            foreach (var polje in poljaLPodmornice)
            {
                if (svaPoljaPodmornica.Contains(polje))
                    return false;
            }

            foreach (var polje in poljaLPodmornice)
            {
                var susedi = DobijSusede(polje);

                foreach (var sused in susedi)
                {
                    if (poljaLPodmornice.Contains(sused))
                        continue;

                    if (svaPoljaPodmornica.Contains(sused))
                        return false;
                }
            }

            return true;
        }

        private List<int> DobijSusede(int polje)
        {
            List<int> susedi = new List<int>();

            int red = (polje - 1) / dimenzija;
            int kol = (polje - 1) % dimenzija;

            int[] deltaRed = { -1, -1, 0, 1, 1, 1, 0, -1 };
            int[] deltaKol = { 0, 1, 1, 1, 0, -1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                int noviRed = red + deltaRed[i];
                int novaKol = kol + deltaKol[i];

                if (noviRed >= 0 && noviRed < dimenzija && novaKol >= 0 && novaKol < dimenzija)
                {
                    int susednoPolje = noviRed * dimenzija + novaKol + 1;
                    susedi.Add(susednoPolje);
                }
            }

            return susedi;
        }

        private bool ValidirajPodmornice()
        {
            return odabranaPolja.Count == brojPolja;
        }

        private async void BtnPosaljiPodmornice_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidirajPodmornice())
            {
                MessageBox.Show("Podmornice nisu validno postavljene! Svaka podmornica mora biti L-oblika i ne smeju se dodirivati.");
                return;
            }

            try
            {
                string poruka = $"{mojId}|";
                string poruka1 = string.Join(",", svaPoljaPodmornica.OrderBy(p => p));
                poruka += poruka1;
                byte[] data = Encoding.UTF8.GetBytes(poruka);
                Log($"{poruka}");

                await Task.Run(() =>
                {
                    tcpSocket.Blocking = true;
                    tcpSocket.Send(data);
                    tcpSocket.Blocking = false;
                });

                Log("Podmornice poslate serveru");

                byte[] buffer = new byte[1024];
                int bytesRead = await Task.Run(() =>
                {
                    tcpSocket.Blocking = true;
                    int bytes = tcpSocket.Receive(buffer);
                    tcpSocket.Blocking = false;
                    return bytes;
                });

                string odgovor = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (odgovor == "PODMORNICE_PRIMLJENE")
                {
                    Log("Server potvrdio podmornice, čekam početak igre...");
                    grpPodmornice.Visibility = Visibility.Collapsed;

                    await Task.Delay(1000);
                    PokreniIgru();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri slanju podmornica: {ex.Message}");
            }
        }

        private void PokreniIgru()
        {
            grpIgra.Visibility = Visibility.Visible;

            for (int i = 1; i <= brIgraca; i++)
            {
                if (i != mojId)
                {
                    cmbProtivnici.Items.Add($"Igrač {i}");
                }
            }

            pollTimer?.Stop();
            pollTimer = new DispatcherTimer();
            pollTimer.Interval = TimeSpan.FromMilliseconds(100);
            pollTimer.Tick += async (s, e) =>
            {
                try
                {
                    if (tcpSocket != null && tcpSocket.Poll(0, SelectMode.SelectRead))
                    {
                        if (tcpSocket.Available > 0)
                        {
                            await PrimiPoruku();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Greška polling: {ex.Message}");
                }
            };
            pollTimer.Start();

            Log("Igra je počela!");
        }

        private async Task PrimiPoruku()
        {
            try
            {
                byte[] buffer = new byte[4096];
                int bytesRead = await Task.Run(() => tcpSocket.Receive(buffer));

                if (bytesRead == 0)
                {
                    Log("Veza sa serverom prekinuta");
                    pollTimer?.Stop();
                    return;
                }

                string poruka = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (poruka == "TVOJ_POTEZ")
                {
                    mojRed = true;
                    txtRezultat.Text = "TVOJ POTEZ! Izaberi protivnika i gađaj!";
                    txtRezultat.Foreground = Brushes.Green;
                    Log("Tvoj potez!");
                }
                else if (poruka == "CEKAJ_RED")
                {
                    mojRed = false;
                    txtRezultat.Text = "Čekaj svoj red...";
                    txtRezultat.Foreground = Brushes.Gray;
                    Log("Čekaj svoj red");
                }
                else if (poruka == "NIJE_TVOJ_POTEZ")
                {
                    MessageBox.Show("Nije tvoj potez! Sačekaj red.");
                    Log("Pokušaj gađanja van poteza!");
                }
                else if (poruka.StartsWith("TABLA:"))
                {
                    string tabla = poruka.Substring(6);
                    PrikaziTablu(tabla);
                }
                else if (poruka.StartsWith("REZULTAT:"))
                {
                    ObradiRezultat(poruka);
                }
                else if (poruka.StartsWith("KRAJ:"))
                {
                    //Kraj igre
                }
            }
            catch (SocketException se)
            {
                if (se.SocketErrorCode != SocketError.WouldBlock)
                {
                    Log($"Socket greška: {se.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"Greška pri primanju: {ex.Message}");
            }
        }

        private void CmbProtivnici_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbProtivnici.SelectedIndex == -1) return;

            string odabir = cmbProtivnici.SelectedItem.ToString();
            trenutniProtivnik = int.Parse(odabir.Split(' ')[1]);

            ZatraziTablu();
        }

        private async void ZatraziTablu()
        {
            try
            {
                string poruka = $"IZABERI:{trenutniProtivnik}";
                byte[] data = Encoding.UTF8.GetBytes(poruka);
                await Task.Run(() => tcpSocket.Send(data));
            }
            catch (Exception ex)
            {
                Log($"Greška pri zahtevanju table: {ex.Message}");
            }
        }

        private void PrikaziTablu(string tablaStr)
        {
            gridTabla.Children.Clear();
            dugmadTabla.Clear();
            gridTabla.Rows = dimenzija;
            gridTabla.Columns = dimenzija;

            string[] redovi = tablaStr.Split(';');
            int poljeBroj = 1;

            for (int i = 0; i < dimenzija; i++)
            {
                string[] polja = redovi[i].Split(',');

                for (int j = 0; j < dimenzija; j++)
                {
                    Button btn = new Button
                    {
                        Content = polja[j],
                        Margin = new Thickness(2),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Tag = poljeBroj
                    };

                    if (polja[j] == "#")
                    {
                        btn.Background = Brushes.LightGray;
                    }
                    else if (polja[j] == "X")
                    {
                        btn.Background = Brushes.Red;
                        btn.Foreground = Brushes.White;
                    }
                    else
                    {
                        btn.Background = Brushes.LightBlue;
                    }

                    btn.Click += BtnGadjaj_Click;
                    dugmadTabla.Add(btn);
                    gridTabla.Children.Add(btn);
                    poljeBroj++;
                }
            }
        }

        private async void BtnGadjaj_Click(object sender, RoutedEventArgs e)
        {
            if (!mojRed) return;

            Button btn = sender as Button;
            int polje = (int)btn.Tag;

            try
            {
                string poruka = $"GADJAJ:{trenutniProtivnik}:{polje}";
                byte[] data = Encoding.UTF8.GetBytes(poruka);
                await Task.Run(() => tcpSocket.Send(data));

                mojRed = false;
            }
            catch (Exception ex)
            {
                Log($"Greška pri gađanju: {ex.Message}");
            }
        }

        private void ObradiRezultat(string poruka)
        {
            string[] delovi = poruka.Split(':');
            string rezultat = delovi[1];
            bool ponovnoPucanje = delovi.Length > 2 && delovi[2] == "PONOVO";

            if (rezultat.Contains("PROMASIO"))
            {
                txtRezultat.Text = "PROMAŠIO! Čekaj svoj red...";
                txtRezultat.Foreground = Brushes.Orange;
                Log("Promašio si!");
                mojRed = false;
            }
            else if (rezultat.Contains("POGODIO"))
            {
                txtRezultat.Text = "POGODIO! Pucaj ponovo!";
                txtRezultat.Foreground = Brushes.Green;
                Log("Pogodio si!");
                mojRed = true;
            }
            else if (rezultat.Contains("POTOPIO"))
            {
                txtRezultat.Text = "POTOPIO! Pucaj ponovo!";
                txtRezultat.Foreground = Brushes.DarkGreen;
                Log("Potopiо si podmornicu!");
                mojRed = true;
            }
            else if (rezultat == "VEC_GADJANO")
            {
                txtRezultat.Text = "Već gađano polje!";
                txtRezultat.Foreground = Brushes.Gray;
            }

            if (trenutniProtivnik != -1)
            {
                ZatraziTablu();
            }
        }

        private void Log(string poruka)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {poruka}\n");
                txtLog.ScrollToEnd();
            });
        }
    }
}