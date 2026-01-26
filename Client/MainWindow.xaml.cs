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
        private List<Button> dugmadPodmornice = new List<Button>();
        private List<Button> dugmadTabla = new List<Button>();
        private int trenutniProtivnik = -1;
        private DispatcherTimer pollTimer;
        private bool mojaVrsta = true;

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

                byte[] buffer = new byte[1024];
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                int bytesReceived = await Task.Run(() => udpSocket.ReceiveFrom(buffer, ref remoteEP));
                string odgovor = Encoding.UTF8.GetString(buffer, 0, bytesReceived);

                if (odgovor.StartsWith("TCP:"))
                {
                    int tcpPort = int.Parse(odgovor.Split(':')[1]);
                    Log($"Primljena TCP informacija, port: {tcpPort}");

                    await PoveziSeNaTcp(serverIp, tcpPort);
                }

                udpSocket.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri prijavi: {ex.Message}");
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

                tcpSocket.Blocking = false;             // ovo sam postavio radi pollinga

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
                txtInfo.Text = poruka;

                grpPodmornice.Visibility = Visibility.Visible;
                KreirajGridPodmornice();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri TCP povezivanju: {ex.Message}");
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
                odabranaPolja.Remove(polje);
                btn.Background = SystemColors.ControlBrush;
            }
            else
            {
                if (odabranaPolja.Count < 9)
                {
                    odabranaPolja.Add(polje);
                    btn.Background = Brushes.LightBlue;
                }
            }

            odabranaPolja.Sort();
            txtOdabranaPolja.Text = string.Join(", ", odabranaPolja);
            btnPosaljiPodmornice.IsEnabled = odabranaPolja.Count == 9 && ValidirajPodmornice();
        }

        private bool ValidirajPodmornice()
        {
            return true;
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
                string poruka = string.Join(",", odabranaPolja);
                byte[] data = Encoding.UTF8.GetBytes(poruka);

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

            for (int i = 1; i <= 10; i++)
            {
                if (i != mojId)
                {
                    cmbProtivnici.Items.Add($"Igrač {i}");
                }
            }

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

                if (poruka.StartsWith("TABLA:"))
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
                    // Kraj igre
                }
            }
            catch (SocketException se)
            {
                if (se.SocketErrorCode != SocketError.WouldBlock)
                {
                    Log($"Socket greška pri primanju poruke: {se.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"Greška pri primanju poruke: {ex.Message}");
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
            if (!mojaVrsta) return;

            Button btn = sender as Button;
            int polje = (int)btn.Tag;

            try
            {
                string poruka = $"GADJAJ:{trenutniProtivnik}:{polje}";
                byte[] data = Encoding.UTF8.GetBytes(poruka);
                await Task.Run(() => tcpSocket.Send(data));

                mojaVrsta = false;
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
                txtRezultat.Text = "PROMAŠIO!";
                Log("Promašio si!");
            }
            else if (rezultat.Contains("POGODIO"))
            {
                txtRezultat.Text = "POGODIO! Pucaj ponovo!";
                Log("Pogodio si!");
            }
            else if (rezultat.Contains("POTOPIO"))
            {
                txtRezultat.Text = "POTOPIO! Pucaj ponovo!";
                Log("Potopiо si podmornicu!");
            }
            else if (rezultat == "VEC_GADJANO")
            {
                txtRezultat.Text = "Već gađano polje!";
            }

            // dodati i logiku za eliminisanje protivnika ako je potopljen

            mojaVrsta = ponovnoPucanje;

            if (mojaVrsta)
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