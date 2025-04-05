using Npgsql;
using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace threadProject
{
	public partial class CustomerPanel : Form
	{
		Customer customer;
		DataTable dt = new DataTable();
		DataTable temp = new DataTable();

		public CustomerPanel(Customer customer)
		{
			InitializeComponent();

			this.customer = customer;
			label1.Text = customer.CustomerName; 
			label6.Text = "Budget:" + Convert.ToString(customer.Budget); 
			label7.Text = "Total Spent:" + Convert.ToString(customer.TotalSpent);
			this.Text = customer.CustomerName + " Panel";
			InitializeBackground();
			GlobalEvents.ProductListChanged += OnProductListChanged;
			GlobalEvents.CustomerPanelReloadRequired += ReloadCustomerPanel;
		}

		private void ReloadCustomerPanel()
		{
			if (this.Visible)
			{
				// Customer bilgilerini yeniden yükle
				UpdateCustomerInfo();
			}
		}

		private void UpdateCustomerInfo()
		{
			if (this.InvokeRequired)
			{
				// UI thread dışında çalışıyorsak, UI thread'e dön
				this.Invoke(new Action(UpdateCustomerInfo));
				return;
			}

			// UI thread'de çalışıyoruz, işlemleri burada yapabiliriz
			if (customer.CustomerType == "Premium")
			{
				string projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\"));
				pictureBox1.BackgroundImage = Image.FromFile(Path.Combine(projectDir, "images", "lightning.png"));
				pictureBox1.BackgroundImageLayout = ImageLayout.Stretch;
				label1.ForeColor = Color.Orange;
			}

			label6.Text = "Budget: " + Convert.ToString(customer.Budget);
			label7.Text = "Total Spent: " + Convert.ToString(customer.TotalSpent);
		}



		private void InitializeBackground()
		{
			string projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\"));
			string backgroundPath = Path.Combine(projectDir, "images", "nvyBlue.jpg");
			this.BackgroundImage = Image.FromFile(backgroundPath);
			this.BackgroundImageLayout = ImageLayout.Stretch;

			if (customer.CustomerType == "Premium")
			{
				pictureBox1.BackgroundImage = Image.FromFile(Path.Combine(projectDir, "images", "lightning.png"));
				pictureBox1.BackgroundImageLayout = ImageLayout.Stretch;
				label1.ForeColor = Color.Orange;
			}
		}
		private async void OnProductListChanged()
		{
			// Ürün listesi değiştiğinde comboBox güncellenir
			await LoadProductsToComboBoxAsync();
		}

		private void AddPanelBtn_Click(object sender, EventArgs e)
		{
			panel3.Visible = !panel3.Visible;
		}

		private async void CustomerPanel_Load(object sender, EventArgs e)
		{
			await LoadProductsToComboBoxAsync();
			await LoadCustomerOrdersAsync();
		}

		private async Task LoadProductsToComboBoxAsync()
		{
			comboBox1.Items.Clear();
			string query = "SELECT ProductName FROM Products";

			try
			{
				using (var connection = new NpgsqlConnection(Program.connectionString))
				{
					await connection.OpenAsync();
					using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
					using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
					{
						while (await reader.ReadAsync())
						{
							comboBox1.Items.Add(reader.GetString(0));
						}
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Ürünler yüklenirken hata: " + ex.Message);
			}
		}

		private void orderBtn_Click(object sender, EventArgs e)
		{
			label4.Visible = false;
			_ = orderProcessAsync(); // async void yerine fire and forget
		}

		private async Task orderProcessAsync()
		{
			if(Convert.ToInt16(textBox1.Text) > 5) 
			{
				MessageBox.Show("A customer's max ordering product quantity is 5 ");
				return;
			}
			if (comboBox1.SelectedItem == null || string.IsNullOrWhiteSpace(textBox1.Text))
			{
				MessageBox.Show("Lütfen ürün seçin ve miktar girin.");
				return;
			}

			if (!int.TryParse(textBox1.Text, out int quantity) || quantity <= 0)
			{
				MessageBox.Show("Geçerli bir miktar girin.");
				return;
			}

			string selectedProductName = comboBox1.SelectedItem.ToString();
			int productId = 0;
			decimal price = 0;

			try
			{
				string queryGetProduct = "SELECT ProductId, Price FROM Products WHERE ProductName = @ProductName";
				using (NpgsqlCommand cmd = new NpgsqlCommand(queryGetProduct, Program.connection))
				{
					cmd.Parameters.AddWithValue("@ProductName", selectedProductName);

					if (Program.connection.State != System.Data.ConnectionState.Open)
					{
						await Program.connection.OpenAsync();
					}

					using (var reader = await cmd.ExecuteReaderAsync())
					{
						if (await reader.ReadAsync())
						{
							productId = reader.GetInt32(0);
							price = reader.GetDecimal(1);
						}
					}
				}

				// Order nesnesi oluştur
				Order order = new Order(customer, productId, quantity, price);

				// Siparişi veritabanına ekle
				bool isInserted = await order.InsertToDatabaseAsync(Program.connection);
				if (isInserted)
				{
					// Log ekle
					await order.AddOrderLogAsync(Program.connection, customer.CustomerName, selectedProductName);
					GlobalEvents.OnLogAdded();
					GlobalEvents.OnOrderAdded();
					label4.Visible = true;
					label4.Text = "The order is in queue.";
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Sipariş eklenirken hata: " + ex.Message);
			}

			await LoadCustomerOrdersAsync();
		}

		private async Task LoadCustomerOrdersAsync()
		{
			string query = "SELECT OrderID, ProductID, Quantity, TotalPrice, OrderDate, OrderStatus FROM Orders WHERE CustomerID = @CustomerId";

			try
			{
				dt.Clear(); // Eski verileri temizle

				// Yeni bir bağlantı oluştur
				using (NpgsqlConnection newConnection = new NpgsqlConnection(Program.connectionString))
				{
					await newConnection.OpenAsync();

					using (NpgsqlCommand cmd = new NpgsqlCommand(query, newConnection))
					{
						cmd.Parameters.AddWithValue("@CustomerId", customer.CustomerId);

						using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
						{
							dt.Load(reader);
						}
					}
				}

				temp = dt.Copy();
				dataGridView1.DataSource = temp;
			}
			catch (Exception ex)
			{
				MessageBox.Show("Siparişler yüklenirken hata: " + ex.Message);
			}
		}
	}
}
