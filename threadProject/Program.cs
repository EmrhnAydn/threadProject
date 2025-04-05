using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Npgsql;
using System.IO;
using System.Threading;

namespace threadProject
{
	internal static class Program
	{
		public static NpgsqlConnection connection;
		public static string connectionString = "Host=localhost;Port=5432;Database=Threads;Username=postgres;Password=2861";
		public static List<Customer> customersList = new List<Customer>();
		public static List<Order> orderList = new List<Order>();
		public static Admin a = new Admin();
		public static SemaphoreSlim dbSemaphore = new SemaphoreSlim(1, 1);

		[STAThread]
		static async Task Main()
		{
			GlobalEvents.OrderApproved += async () =>
			{
				// Onaylanan siparişler işleme alınır
				await createThreadsForProcessingOrders();
			};

			try
			{
				connection = new NpgsqlConnection(connectionString);
				await connection.OpenAsync(); // Bağlantıyı asenkron aç
				await clearTablesAsync();
				await createCustomersAsync();
				await resetStockAsync();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Veritabanı bağlantı hatası: {ex.Message}");
			}
			InitializeWorkerThreads(1);

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm(customersList));
		}

		private static void InitializeWorkerThreads(int threadCount)
		{
			for (int i = 0; i < threadCount; i++)
			{
				WorkerThread wt = new WorkerThread();
				workerThreads.Add(wt);
				wt.Start();
			}

		}

		private static async Task clearTablesAsync()
		{
			string query = @"
				TRUNCATE TABLE Logs CASCADE;
                TRUNCATE TABLE Orders CASCADE;
                TRUNCATE TABLE Customers CASCADE;	
				TRUNCATE TABLE Products CASCADE;
            ";

			try
			{
				using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
				{
					await command.ExecuteNonQueryAsync();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Tablolar temizlenirken hata oluştu: {ex.Message}");
			}
		}

		private static async Task resetStockAsync()
		{
			string query = @"	TRUNCATE TABLE Products RESTART IDENTITY CASCADE;
							INSERT INTO Products (ProductName, Stock, Price) 
									VALUES 
										('Product1', 500, 100),
									    ('Product2', 10, 50),
										('Product3', 200, 45),
										('Product4', 75, 75),
										('Product5', 0, 500)";

			try
			{
				using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
				{
					await command.ExecuteNonQueryAsync();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Stok sıfırlama sırasında hata oluştu: {ex.Message}");
			}
		}

		private static async Task createCustomersAsync()
		{
			Random random = new Random();
			int customerCount = random.Next(5, 11); // 5-10 arası müşteri sayısı
			int premiumCount = 0;

			customersList.Clear();

			for (int i = 0; i < customerCount; i++)
			{
				decimal budget = random.Next(500, 3001);
				string customerType = "Standard";
				if (budget >= 2000 && (premiumCount < 2 || random.NextDouble() > 0.5))
				{
					customerType = "Premium";
					premiumCount++;
				}

				string customerName = $"Customer_{i + 1}";
				Customer customer = new Customer(customerName, budget, customerType, i);
				customersList.Add(customer);
			}

			// customer id refreshing
			{
				string query = "TRUNCATE TABLE Customers RESTART IDENTITY CASCADE;";
				using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
				{
					await command.ExecuteNonQueryAsync();
				}
			}

			// Customers tablosuna ekleme
			foreach (var customer in customersList)
			{
				string query = "INSERT INTO Customers (CustomerName, Budget, CustomerType, TotalSpent) " +
							   "VALUES (@CustomerName, @Budget, @CustomerType, @TotalSpent)";

				using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
				{
					command.Parameters.AddWithValue("@CustomerName", customer.CustomerName);
					command.Parameters.AddWithValue("@Budget", customer.Budget);
					command.Parameters.AddWithValue("@CustomerType", customer.CustomerType);
					command.Parameters.AddWithValue("@TotalSpent", customer.TotalSpent);

					await command.ExecuteNonQueryAsync();
				}
			}

			// CustomerID'leri çekme
			foreach (var customer in customersList)
			{
				string cName = customer.CustomerName;
				string query = "SELECT CustomerId FROM Customers WHERE CustomerName = @cName LIMIT 1";

				using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
				{
					command.Parameters.AddWithValue("@cName", cName);
					object result = await command.ExecuteScalarAsync();
					if (result != null && result != DBNull.Value)
					{
						customer.CustomerId = Convert.ToInt32(result);
					}
				}
			}

			string createAdmin = "INSERT INTO Customers (CustomerName, Budget, CustomerType, TotalSpent) " +
					"VALUES ('admin', 0, 'Premium', 0);";

			using (NpgsqlCommand command = new NpgsqlCommand(createAdmin, connection))
			{
				await command.ExecuteNonQueryAsync();
			}

		}

		public static async Task createCustomerPanel(Customer c)
		{
			// Asenkron form açmak tam olarak anlamlı değil, ama metodu async bırakalım.
			await Task.Yield();
			CustomerPanel customerPanel = new CustomerPanel(c);
			customerPanel.Show();
		}

		public static async Task createAdminPanel()
		{
			await Task.Yield();
			AdminPanel adminPanel = new AdminPanel(a);
			adminPanel.Show();
		}



		private static List<WorkerThread> workerThreads = new List<WorkerThread>();
		private static readonly object _processLock = new object();
		private static Queue<Order> waitingOrders = new Queue<Order>();
		
		public static List<Order> processingOrders = new List<Order>();

		public static async Task createThreadsForProcessingOrders()
		{
			// 1) Processing durumundaki siparişleri çek
			processingOrders = await Order.GetProcessingOrdersAsync(connection);
			

			// 2) Her order için priority hesapla
			foreach (var order in processingOrders)
			{
				order.UpdatePriority();
			}

			// 3) PriorityScore'a göre sırala (yüksekten düşüğe)
			processingOrders.Sort((o1, o2) => o2.PriorityScore.CompareTo(o1.PriorityScore));
			int i = processingOrders.Count;

			await GlobalEvents.UpdatePriorityList(processingOrders);
			await Task.Delay(500);
			// 4) İşlem görmeyen siparişleri thread’lere atamak
			for (int j = 0; j < i; j++)
			{
					Order order = processingOrders[0];

					// "Running" durumuna geçirme işlemi
					await Order.UpdateOrderStatusAsync(order.OrderId, "Running", connection);

					bool isAssigned = false;

					lock (_processLock)
					{
						// Uygun (boş) bir thread bul
						var freeThread = workerThreads.FirstOrDefault(wt => wt.IsBusy == false);
						if (freeThread != null)
						{
							// Siparişi boş thread'e ata
							freeThread.EnqueueOrder(order);
							isAssigned = true;
						}
						else
						{
							// Eğer boş thread bulunamazsa kuyruğa ekle
							waitingOrders.Enqueue(order);
						}

					}

					
					if (!isAssigned)
					{
						Console.WriteLine($"Order {order.OrderId} added to waiting queue.");
					}
				processingOrders.Remove(order);
				foreach(var ord in processingOrders)
				{ 
					ord.UpdatePriority(); 
				}
				processingOrders.Sort((o1, o2) => o2.PriorityScore.CompareTo(o1.PriorityScore));

				// send the priotery score to mainform
				await GlobalEvents.UpdatePriorityList(processingOrders);
				await Task.Delay(1000);
			}

			// 5) Thread boşaldığında bekleme kuyruğunu kontrol et
			AssignOrdersFromQueue();
		}

		/// 
		/// Bekleme kuyruğundaki siparişleri boşalan thread'lere atar.
		/// 
		private static void AssignOrdersFromQueue()
		{
			lock (_processLock)
			{
				while (waitingOrders.Count > 0)
				{
					var freeThread = workerThreads.FirstOrDefault(wt => wt.IsBusy == false);
					if (freeThread != null)
					{
						var order = waitingOrders.Dequeue();
						freeThread.EnqueueOrder(order);
						Console.WriteLine($"Order {order.OrderId} assigned to a thread from the waiting queue.");
					}
					else
					{
						// Eğer hâlâ boş thread yoksa döngüden çık
						break;
					}
				}
			}
		}



	/*	public static async Task RemoveOrderAndResortAsync(Order order)
		{
			// 1) Mevcut listeden (orderList veya processingOrders) order’ı çıkar
			if (processingOrders.Contains(order))
			{
				processingOrders.Remove(order);
			}

			// 2) Kalan siparişlerin priority’sini güncelle
			foreach (var ord in processingOrders)
			{
				ord.UpdatePriority();
			}

			// 3) PriorityScore'a göre sıralama (yüksekten düşüğe)
			processingOrders.Sort((o1, o2) => o2.PriorityScore.CompareTo(o1.PriorityScore));

			// 4) MainForm’daki ListBox’ı güncellemek için GlobalEvents üzerinden haber ver
			await GlobalEvents.UpdatePriorityList(processingOrders);
		}	*/



		public static async void closeProgram()
		{
			foreach (var t in workerThreads)
			{
				t.Stop();
			}
			Application.Exit();

		}

	}
}
