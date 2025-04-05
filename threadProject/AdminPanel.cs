using Npgsql;
using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

namespace threadProject
{
	public partial class AdminPanel : Form
	{
		Admin a;
		public AdminPanel(Admin a)
		{
			InitializeComponent();
			this.a = a;

			string projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\"));
			string backgroundPath = Path.Combine(projectDir, "images", "nvyBlue.jpg");
			this.BackgroundImage = Image.FromFile(backgroundPath);
			this.BackgroundImageLayout = ImageLayout.Stretch;

			
			GlobalEvents.OrderAdded += OnOrderAdded;
			GlobalEvents.LogAdded += OnLogAdded;
			GlobalEvents.OrderCancelled += OnLogCancelled;
			GlobalEvents.ProcessSuccessful += OnLogSuccess;

			panel7.Location = addingProductPanel.Location;
			deletePanel.Location = addingProductPanel.Location;

		}

		

		private async void AdminPanel_Load(object sender, EventArgs e)
		{
			await LoadProductsToComboBox();
			await fillDataTable();
			await fillOrderTable();
			await LoadLogs(); // Uygulama açıldığında mevcut logları yükle
		}

		private async void OnLogAdded()
		{
			// Her log eklendiğinde logları güncelle
			await LoadLogs();
		}
		private async void OnOrderAdded()
		{
			// Yeni bir sipariş eklendi, orderDataGridView'i güncelle
			await fillOrderTable();
		}

		private async Task OnLogCancelled(Order order)
		{
			await LoadLogs();
			await fillOrderTable();
		}
		private async Task OnLogSuccess(Order order)
		{
			await LoadLogs();
			await fillOrderTable();
			await fillDataTable();

		}


		private async Task LoadLogs()
		{
			// Logları geçici bir listede toplayacağız
			List<string> tempLogs = new List<string>();
			string query = "SELECT LogDetails FROM Logs ORDER BY LogID ASC";

			try
			{
				// 1) Yeni bağlantı oluştur
				using (var connection = new NpgsqlConnection(Program.connectionString))
				{
					await connection.OpenAsync();
					using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
					using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
					{
						while (await reader.ReadAsync())
						{
							tempLogs.Add(reader.GetString(0));
						}
					}
				}

				// 2) logslistBox güncellemeyi UI thread'de yap
				// Bu kodun hangi iş parçacığında çalıştığını kesinleştirmek için:
				if (this.logslistBox.InvokeRequired)
				{
					// Şu an arka plan thread'indeyiz => UI thread'e geçiyoruz
					this.logslistBox.Invoke(new Action(() =>
					{
						logslistBox.Items.Clear();
						foreach (var detail in tempLogs)
						{
							logslistBox.Items.Add(detail);
						}
						if (logslistBox.Items.Count > 0)
						{
							logslistBox.TopIndex = logslistBox.Items.Count - 1;
						}
					}));
				}
				else
				{
					// Zaten UI thread'indeysek doğrudan güncelle
					logslistBox.Items.Clear();
					foreach (var detail in tempLogs)
					{
						logslistBox.Items.Add(detail);
					}
					if (logslistBox.Items.Count > 0)
					{
						logslistBox.TopIndex = logslistBox.Items.Count - 1;
					}
				}
			}
			catch (Exception ex)
			{
				// Hata durumunda da UI thread’den mesaj göstermek gerekir
				if (this.InvokeRequired)
				{
					this.Invoke(new Action(() =>
					{
						MessageBox.Show("Loglar yüklenirken hata: " + ex.Message);
					}));
				}
				else
				{
					MessageBox.Show("Loglar yüklenirken hata: " + ex.Message);
				}
			}
		}


		private async Task LoadProductsToComboBox()
		{
			comboBox1.Items.Clear();
			comboBox2.Items.Clear();

			string query = "SELECT ProductName FROM Products";

			if (Program.connection.State != System.Data.ConnectionState.Open)
			{
				await Program.connection.OpenAsync();
			}

			using (NpgsqlCommand command = new NpgsqlCommand(query, Program.connection))
			{
				using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
				{
					while (await reader.ReadAsync())
					{
						comboBox1.Items.Add(reader.GetString(0));
						comboBox2.Items.Add(reader.GetString(0));
					}
				}
			}
		}

		public async Task fillDataTable()
		{
			// 1) Arkaplanda verileri çek
			DataTable dt = await Task.Run(async () =>
			{
				DataTable localDt = new DataTable();
				string query = "SELECT * FROM Products";
				try
				{
					using (var connection = new NpgsqlConnection(Program.connectionString))
					{
						await connection.OpenAsync();

						using (var command = new NpgsqlCommand(query, connection))
						using (var reader = await command.ExecuteReaderAsync())
						{
							localDt.Load(reader); // Verileri DataTable'a yükle
						}
					}
				}
				catch (Exception ex)
				{
					// Arka planda hata
					// Bu hata UI dışında, ama yine de metot sonunda yakalayıp gösteririz
					throw new Exception($"Database Error: {ex.Message}", ex);
				}

				return localDt;
			});

			// 2) DataTable sonuçlarını UI’da göster
			try
			{
				if (dt.Rows.Count == 0)
				{
					// UI thread’de mesaj göstermek
					if (this.InvokeRequired)
					{
						this.Invoke(new Action(() =>
						{
							MessageBox.Show("No data found in the Products table.");
						}));
					}
					else
					{
						MessageBox.Show("No data found in the Products table.");
					}
					return;
				}

				// DataGridView güncelleme
				if (dataGridView1.InvokeRequired)
				{
					dataGridView1.Invoke(new Action(() =>
					{
						UpdateProductsDataGridView(dt);
					}));
				}
				else
				{
					UpdateProductsDataGridView(dt);
				}
			}
			catch (Exception ex)
			{
				// UI güncellemeye çalışırken hata
				MessageBox.Show($"UI Error: {ex.Message}");
			}
		}

		/// <summary>
		/// DataGridView üzerinde Products bilgilerini güncelleme (UI) kısmı.
		/// </summary>
		private void UpdateProductsDataGridView(DataTable dt)
		{
			dt.DefaultView.Sort = "productid ASC";
			dataGridView1.AutoGenerateColumns = true;
			dataGridView1.DataSource = dt.DefaultView.ToTable(); // Sıralanmış görünümü tabloya dönüştür
			dataGridView1.Refresh();
		}



		private void AddPanelBtn_Click(object sender, EventArgs e)
		{
			if(addingProductPanel.Visible == false)
			{
				addingProductPanel.Visible = true;
				panel7.Visible = false;
				deletePanel.Visible = false;
			}
			else
			{
				addingProductPanel.Visible=false;
			}
		}

		private void dltPrdctBtn_Click(object sender, EventArgs e)
		{
			//deletePanel.Visible = !deletePanel.Visible;
			if(deletePanel.Visible == false)
			{
				deletePanel.Visible = true;
				panel7.Visible = false;
				addingProductPanel.Visible = false ;
			}
			else
			{
				deletePanel.Visible = false;
			}
		}

		private async void addBtn_Click(object sender, EventArgs e)
		{
			await addProduct();
			await fillDataTable();
			await LoadProductsToComboBox();
			await addProductLog();
			GlobalEvents.OnLogAdded();
		}

		private async Task addProductLog()
		{
			
			// Log sorgusu:
			string logQuery = @"
        INSERT INTO Logs (CustomerId, OrderId, LogDate, LogType, LogDetails)
        VALUES (@CustomerId, @OrderId, @LogDate, @LogType, @LogDetails)
    ";

			try
			{
				// Eğer bağlantı kapalıysa açalım
				if (Program.connection.State != System.Data.ConnectionState.Open)
				{
					await Program.connection.OpenAsync();
				}

				using (var cmd = new NpgsqlCommand(logQuery, Program.connection))
				{
					DateTime dTime = DateTime.Now;
					cmd.Parameters.AddWithValue("@CustomerId", Program.customersList.Count + 1);
					cmd.Parameters.AddWithValue("@OrderId", DBNull.Value); // OrderId null olacak
					cmd.Parameters.AddWithValue("@LogDate", dTime);
					cmd.Parameters.AddWithValue("@LogType", "added product");
					// LogDetails için metin formatı:
					string logDetails = $"{dTime} | Admin added {textBox2.Text} {textBox1.Text}, one of which costs {textBox3.Text} TRY";
					cmd.Parameters.AddWithValue("@LogDetails", logDetails);

					await cmd.ExecuteNonQueryAsync();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Log ekleme hatası: " + ex.Message);
			}
		}

		private async Task addProduct()
		{
			string productName = textBox1.Text.Trim();

			if (string.IsNullOrWhiteSpace(productName))
			{
				MessageBox.Show("Lütfen geçerli bir ürün adı girin.");
				return;
			}

			if (!int.TryParse(textBox2.Text, out int stock) || stock < 0)
			{
				MessageBox.Show("Lütfen geçerli bir stok miktarı girin.");
				return;
			}

			if (!decimal.TryParse(textBox3.Text, out decimal price) || price < 0)
			{
				MessageBox.Show("Lütfen geçerli bir fiyat girin.");
				return;
			}

			string query = "INSERT INTO Products (ProductName, Stock, Price) VALUES (@ProductName, @Stock, @Price)";

			try
			{
				if (Program.connection.State != System.Data.ConnectionState.Open)
				{
					await Program.connection.OpenAsync();
				}

				using (NpgsqlCommand cmd = new NpgsqlCommand(query, Program.connection))
				{
					cmd.Parameters.AddWithValue("@ProductName", productName);
					cmd.Parameters.AddWithValue("@Stock", stock);
					cmd.Parameters.AddWithValue("@Price", price);
					await cmd.ExecuteNonQueryAsync();
				}
				GlobalEvents.OnProductListChanged();
				MessageBox.Show("Ürün başarıyla eklendi.");
			}
			catch (Exception ex)
			{
				MessageBox.Show("Hata: " + ex.Message);
			}
		}

		private async void dltBtn_Click(object sender, EventArgs e)
		{
			await deleteProduct();
			await LoadProductsToComboBox();
			await dltLog();
			GlobalEvents.OnLogAdded();
		}

		private async Task dltLog()
		{
			
			// Log sorgusu:
			string logQuery = @"
        INSERT INTO Logs (CustomerId, OrderId, LogDate, LogType, LogDetails)
        VALUES (@CustomerId, @OrderId, @LogDate, @LogType, @LogDetails)
    ";


			try
			{
				if (Program.connection.State != System.Data.ConnectionState.Open)
				{
					await Program.connection.OpenAsync();
				}

				using (var cmd = new NpgsqlCommand(logQuery, Program.connection))
				{
					DateTime dTimeXd = DateTime.Now;
					cmd.Parameters.AddWithValue("@CustomerId", Program.customersList.Count + 1);
					cmd.Parameters.AddWithValue("@OrderId", DBNull.Value); // OrderId null olacak
					cmd.Parameters.AddWithValue("@LogDate", dTimeXd);
					cmd.Parameters.AddWithValue("@LogType", "deleted product");

					// LogDetails için metin formatı:
					string logDetails = $"{dTimeXd} | Admin deleted {comboBox1.Text}";
					cmd.Parameters.AddWithValue("@LogDetails", logDetails);

					await cmd.ExecuteNonQueryAsync();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Log ekleme hatası: " + ex.Message);
			}
		}

		private async Task deleteProduct()
		{
			if (comboBox1.SelectedItem == null)
			{
				MessageBox.Show("Lütfen silmek istediğiniz ürünü seçin.");
				return;
			}

			string productName = comboBox1.SelectedItem.ToString();
			if (string.IsNullOrWhiteSpace(productName))
			{
				MessageBox.Show("Geçerli bir ürün seçin.");
				return;
			}

			string query = "DELETE FROM Products WHERE ProductName = @ProductName";

			try
			{
				if (Program.connection.State != System.Data.ConnectionState.Open)
				{
					await Program.connection.OpenAsync();
				}

				using (NpgsqlCommand cmd = new NpgsqlCommand(query, Program.connection))
				{
					cmd.Parameters.AddWithValue("@ProductName", productName);

					int rowsAffected = await cmd.ExecuteNonQueryAsync();
					if (rowsAffected > 0)
					{
						MessageBox.Show("Ürün başarıyla silindi.");
						await fillDataTable();
						GlobalEvents.OnProductListChanged();

					}
					else
					{
						MessageBox.Show("Ürün bulunamadı veya silinemedi.");
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Hata: " + ex.Message);
			}
		}

		private void button1_Click(object sender, EventArgs e)
		{
			if (panel7.Visible == false)
			{
				panel7.Visible = true;
				addingProductPanel.Visible = false;
				deletePanel.Visible = false;
			}
			else
			{
				panel7.Visible = false;
			}
		}

		private async void stockUpdBtn_Click(object sender, EventArgs e)
		{
			await stockUpdate();
			await fillDataTable();
			await updateStockLog();
			GlobalEvents.OnLogAdded();
		}

		private async Task updateStockLog()
		{
			
			// Log sorgusu:
			string logQuery = @"
        INSERT INTO Logs (CustomerId, OrderId, LogDate, LogType, LogDetails)
        VALUES (@CustomerId, @OrderId, @LogDate, @LogType, @LogDetails)
    ";


			try
			{
				if (Program.connection.State != System.Data.ConnectionState.Open)
				{
					await Program.connection.OpenAsync();
				}

				using (var cmd = new NpgsqlCommand(logQuery, Program.connection))
				{
					DateTime dtime = DateTime.Now;
					cmd.Parameters.AddWithValue("@CustomerId", Program.customersList.Count + 1);
					cmd.Parameters.AddWithValue("@OrderId", DBNull.Value); // OrderId null olacak
					cmd.Parameters.AddWithValue("@LogDate", dtime);
					cmd.Parameters.AddWithValue("@LogType", "update product");
					// LogDetails için metin formatı:
					string logDetails = $"{dtime} | Admin updated {comboBox2.Text}'s stock to {textBox4.Text}";
					cmd.Parameters.AddWithValue("@LogDetails", logDetails);

					await cmd.ExecuteNonQueryAsync();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Log ekleme hatası: " + ex.Message);
			}
		}

		private async Task stockUpdate()
		{
			if (comboBox2.SelectedItem == null)
			{
				MessageBox.Show("Lütfen stok güncellemek istediğiniz ürünü seçin.");
				return;
			}

			string productName = comboBox2.SelectedItem.ToString();
			if (string.IsNullOrWhiteSpace(productName))
			{
				MessageBox.Show("Geçerli bir ürün seçin.");
				return;
			}

			if (!int.TryParse(textBox4.Text.Trim(), out int newStock) || newStock < 0)
			{
				MessageBox.Show("Lütfen geçerli bir stok miktarı girin.");
				return;
			}

			string query = "UPDATE Products SET Stock = @Stock WHERE ProductName = @ProductName";

			try
			{
				if (Program.connection.State != System.Data.ConnectionState.Open)
				{
					await Program.connection.OpenAsync();
				}

				using (NpgsqlCommand cmd = new NpgsqlCommand(query, Program.connection))
				{
					cmd.Parameters.AddWithValue("@Stock", newStock);
					cmd.Parameters.AddWithValue("@ProductName", productName);

					int rowsAffected = await cmd.ExecuteNonQueryAsync();
					if (rowsAffected > 0)
					{
						MessageBox.Show("Stok başarıyla güncellendi.");
						await fillDataTable();
					}
					else
					{
						MessageBox.Show("Ürün bulunamadı veya stok güncellenemedi.");
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Hata: " + ex.Message);
			}
		}

		private async Task fillOrderTable()
		{
			DataTable orderDt = null;

			try
			{
				// 1) Arkaplanda verileri çek (Order.GetAllOrdersAsync zaten asenkron)
				//    Bu metot içinde her sorgu kendine yeni bağlantı açtığını varsayıyoruz.
				orderDt = await Task.Run(async () =>
				{
					// Örneğin veritabanından DataTable getirir
					return await Order.GetAllOrdersAsync();
				});
			}
			catch (Exception ex)
			{
				MessageBox.Show("Veri çekme hatası: " + ex.Message);
				return;
			}

			try
			{
				// 2) UI kontrolünü güncelle (DataGridView)
				if (orderDataGridView.InvokeRequired)
				{
					orderDataGridView.Invoke(new Action(() =>
					{
						orderDt.DefaultView.Sort = "orderid ASC";
						UpdateOrderDataGridView(orderDt);
					}));
				}
				else
				{
					orderDt.DefaultView.Sort = "orderid ASC";
					UpdateOrderDataGridView(orderDt);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("UI güncelleme hatası: " + ex.Message);
			}
		}

		/// <summary>
		/// DataGridView doldurma ve kolon/row ayarları gibi
		/// UI işlemlerini tek bir fonksiyonda topluyoruz.
		/// </summary>
		private void UpdateOrderDataGridView(DataTable orderDt)
		{
			orderDataGridView.DataSource = orderDt;
			// Daha önce eklenmiş mi kontrolü
			if (orderDataGridView.Columns["AcceptColumn"] == null)
			{
				DataGridViewButtonColumn acceptColumn = new DataGridViewButtonColumn
				{
					Name = "AcceptColumn",
					HeaderText = "Action",
					UseColumnTextForButtonValue = false
				};
				orderDataGridView.Columns.Add(acceptColumn);
			}

			// Her satırın OrderStatus durumuna göre buton vs. ayarla
			foreach (DataGridViewRow row in orderDataGridView.Rows)
			{
				var orderStatusValue = row.Cells["orderstatus"].Value?.ToString();
				var actionCell = row.Cells["AcceptColumn"];

				if (orderStatusValue == "Waiting")
				{
					actionCell.Value = "Accept";
					actionCell.ReadOnly = false;  // Tıklanabilir
				}
				else
				{
					actionCell.Value = "Processing";
					actionCell.ReadOnly = true;   // Tıklanamaz
				}
			}
		}



		private async void orderDataGridView_CellClick_1(object sender, DataGridViewCellEventArgs e)
		{
			if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

			if (orderDataGridView.Columns[e.ColumnIndex].Name == "AcceptColumn")
			{
				var cellValue = orderDataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
				// onay kontrolü
				if (cellValue == "Processing")
				{
					return;
				}

				int orderId = Convert.ToInt32(orderDataGridView.Rows[e.RowIndex].Cells["orderid"].Value);
				int customerId = Convert.ToInt32(orderDataGridView.Rows[e.RowIndex].Cells["customerid"].Value);
				int productId = Convert.ToInt32(orderDataGridView.Rows[e.RowIndex].Cells["productid"].Value);
				int quantity = Convert.ToInt32(orderDataGridView.Rows[e.RowIndex].Cells["quantity"].Value);
				decimal price = Convert.ToDecimal(orderDataGridView.Rows[e.RowIndex].Cells["totalprice"].Value);

				// Order sınıfını kullanarak sipariş durumunu güncelle
				bool updated = await Order.UpdateOrderStatusAsync(orderId, "Processing", Program.connection);

				if (updated)
				{
					// mevcut müşteriyi bul
					Customer customer = Program.customersList.FirstOrDefault(c => c.CustomerId == customerId);

					Order newOrder = new Order(customer, productId, quantity, price)
					{
						// OrderId = orderId,
						OrderStatus = "Processing"
					};
					await newOrder.setOrderId(orderId);

					// Program.orderList'e ekle
					Program.orderList.Add(newOrder);
					MessageBox.Show($"Order #{orderId} kabul edildi.");
					orderDataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = "Accepted";
					await Order.AcceptOrderLogAsync(orderId, Program.connection); // log ekleme
					await fillOrderTable();
					GlobalEvents.OnLogAdded();
					GlobalEvents.OnOrderApproved();
				}
				else
				{
					MessageBox.Show("Sipariş güncellenemedi.");
				}
			}

		}

		private async void acceptAllBtn_Click(object sender, EventArgs e)
		{
			try
			{
				string query = "SELECT OrderID, CustomerID, ProductID, Quantity, TotalPrice FROM Orders WHERE OrderStatus = 'Waiting'";
				List<int> processedOrders = new List<int>();

				using (var connection = new NpgsqlConnection(Program.connectionString))
				{
					await connection.OpenAsync();

					// Veritabanından "Waiting" durumundaki siparişleri al
					using (var cmd = new NpgsqlCommand(query, connection))
					using (var reader = await cmd.ExecuteReaderAsync())
					{
						while (await reader.ReadAsync())
						{
							int orderId = reader.GetInt32(0);
							int customerId = reader.GetInt32(1);
							int productId = reader.GetInt32(2);
							int quantity = reader.GetInt32(3);
							decimal price = reader.GetDecimal(4);

							// Müşteri detaylarını bul
							Customer customer = Program.customersList.FirstOrDefault(c => c.CustomerId == customerId);
							if (customer == null)
							{
								MessageBox.Show($"Customer with ID {customerId} not found. Skipping order {orderId}.");
								continue;
							}

							// Her işlem için yeni bir bağlantı oluştur
							using (var updateConnection = new NpgsqlConnection(Program.connectionString))
							{
								await updateConnection.OpenAsync();

								// Sipariş durumunu güncelle
								bool updated = await Order.UpdateOrderStatusAsync(orderId, "Processing", updateConnection);
								if (updated)
								{
									// Yeni Order oluştur ve setOrderId çağır
									Order newOrder = new Order(customer, productId, quantity, price)
									{
										OrderStatus = "Processing"
									};
									await newOrder.setOrderId(orderId);

									// Program.orderList'e ekle
									Program.orderList.Add(newOrder);

									// Log ekle
									using (var logConnection = new NpgsqlConnection(Program.connectionString))
									{
										await logConnection.OpenAsync();
										await Order.AcceptOrderLogAsync(orderId, logConnection);
									}

									// İşlenmiş siparişleri kaydet
									processedOrders.Add(orderId);
								}
							}
						}
					}
				}
				MessageBox.Show($"{processedOrders.Count} sipariş kabul edildi.");
				await fillOrderTable(); // Tabloyu güncelle
				GlobalEvents.OnLogAdded();
				GlobalEvents.OnOrderApproved();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Hata: {ex.Message}");
			}
		}


	}
}
