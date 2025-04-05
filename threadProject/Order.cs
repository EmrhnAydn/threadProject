using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;

namespace threadProject
{
	public class Order
	{
		public int OrderId { get; private set; }
		public Customer customer { get; set; }
		public int CustomerId { get; set; }
		public int ProductId { get; set; }
		public int Quantity { get; set; }
		public decimal Price { get; set; } // birim ürün fiyatı
		public decimal TotalPrice => Price * Quantity;
		public DateTime OrderDate { get; set; } = DateTime.Now;
		public string OrderStatus { get; set; } = "Waiting";
		public float PriorityScore { get; private set; }
		public string ProductName { get; set; }

		public float waitedTime { get; set; }	

		public Order(Customer customer, int productId, int quantity, decimal price)
		{
			this.customer = customer;
			CustomerId = customer.CustomerId;
			ProductId = productId;
			Quantity = quantity;
			Price = price;
		}

		public async Task<bool> InsertToDatabaseAsync(NpgsqlConnection connection)
		{
			string queryInsertOrder = @"
                INSERT INTO Orders (CustomerId, ProductId, Quantity, OrderDate, TotalPrice, OrderStatus)
                VALUES (@CustomerId, @ProductId, @Quantity, @OrderDate, @TotalPrice, @OrderStatus)
                RETURNING OrderID;";

			using (NpgsqlCommand cmdInsert = new NpgsqlCommand(queryInsertOrder, connection))
			{
				cmdInsert.Parameters.AddWithValue("@CustomerId", CustomerId);
				cmdInsert.Parameters.AddWithValue("@ProductId", ProductId);
				cmdInsert.Parameters.AddWithValue("@Quantity", Quantity);
				cmdInsert.Parameters.AddWithValue("@OrderDate", OrderDate);
				cmdInsert.Parameters.AddWithValue("@TotalPrice", TotalPrice);
				cmdInsert.Parameters.AddWithValue("@OrderStatus", OrderStatus);

				if (connection.State != ConnectionState.Open)
				{
					await connection.OpenAsync();
				}

				object result = await cmdInsert.ExecuteScalarAsync();
				if (result != null && int.TryParse(result.ToString(), out int newOrderId))
				{
					OrderId = newOrderId;
					return true;
				}
			}

			return false;
		}

		public async Task setOrderId(int id)
		{
			OrderId = id;
		}

		public async Task AddOrderLogAsync(NpgsqlConnection connection, string customerName, string productName)
		{
			ProductName = productName;
			string logInsertQuery = @"
                INSERT INTO Logs (CustomerId, OrderId, LogDate, LogType, LogDetails)
                VALUES (@CustomerId, @OrderId, @LogDate, @LogType, @LogDetails)";

			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync();
			}

			using (NpgsqlCommand cmdLog = new NpgsqlCommand(logInsertQuery, connection))
			{
				cmdLog.Parameters.AddWithValue("@CustomerId", CustomerId);
				cmdLog.Parameters.AddWithValue("@OrderId", OrderId);
				cmdLog.Parameters.AddWithValue("@LogDate", OrderDate);
				cmdLog.Parameters.AddWithValue("@LogType", "Order");
				string logDetails = $"{OrderDate} | {customerName} has been ordering {Quantity} {productName}";
				cmdLog.Parameters.AddWithValue("@LogDetails", logDetails);

				await cmdLog.ExecuteNonQueryAsync();
			}
		}

		/// 
		/// Tüm siparişleri çeker.
		///
		public static async Task<DataTable> GetAllOrdersAsync()
		{
			DataTable orderDt = new DataTable();
			string orderQuery = "SELECT * FROM Orders";

			string connectionString = Program.connectionString; 

			using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
			{
				await connection.OpenAsync();

				using (NpgsqlCommand cmd = new NpgsqlCommand(orderQuery, connection))
				{
					using (var reader = await cmd.ExecuteReaderAsync())
					{
						orderDt.Load(reader);
					}
				}
			}

			return orderDt;
		}


		/// <summary>
		/// Belirli bir siparişin durumunu günceller.
		/// </summary>
		public static async Task<bool> UpdateOrderStatusAsync(int orderId, string newStatus, NpgsqlConnection connection)
		{
			string updateOrderQuery = "UPDATE Orders SET OrderStatus = @Status WHERE OrderID = @OrderID";

			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync();
			}

			using (NpgsqlCommand cmd = new NpgsqlCommand(updateOrderQuery, connection))
			{
				cmd.Parameters.AddWithValue("@Status", newStatus);
				cmd.Parameters.AddWithValue("@OrderID", orderId);

				int rowsAffected = await cmd.ExecuteNonQueryAsync();
				return rowsAffected > 0;
			}
		}

		/// <summary>
		/// Bekleyen tüm siparişleri "Processing" yapar ve güncellenen siparişlerin ID'lerini döndürür.
		/// </summary>
		public static async Task<System.Collections.Generic.List<int>> AcceptAllWaitingOrdersAsync(NpgsqlConnection connection)
		{
			string updateAllQuery = "UPDATE Orders SET OrderStatus = 'Processing' WHERE OrderStatus = 'Waiting' RETURNING OrderID;";
			var updatedOrderIds = new System.Collections.Generic.List<int>();

			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync();
			}

			using (NpgsqlCommand cmd = new NpgsqlCommand(updateAllQuery, connection))
			{
				using (var reader = await cmd.ExecuteReaderAsync())
				{
					while (await reader.ReadAsync())
					{
						updatedOrderIds.Add(reader.GetInt32(0));
					}
				}
			}

			return updatedOrderIds;
		}

		/// <summary>
		/// Log ekleme için sipariş detaylarını geri döndürür.
		/// Müşteri adı, ürün adı, miktar vb.
		/// </summary>
		public static async Task<(int CustomerId, string CustomerName, string ProductName, int Quantity, DateTime OrderDate)> GetOrderDetailsForLogAsync(int orderId, NpgsqlConnection connection)
		{
			string selectQuery = @"
                SELECT o.CustomerID, o.OrderID, o.OrderDate, c.CustomerName, p.ProductName, o.Quantity
                FROM Orders o
                JOIN Customers c ON c.CustomerID = o.CustomerID
                JOIN Products p ON p.ProductID = o.ProductID
                WHERE o.OrderID = @OrderID";

			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync();
			}

			using (NpgsqlCommand cmd = new NpgsqlCommand(selectQuery, connection))
			{
				cmd.Parameters.AddWithValue("@OrderID", orderId);

				using (var reader = await cmd.ExecuteReaderAsync())
				{
					if (await reader.ReadAsync())
					{
						int customerId = reader.GetInt32(reader.GetOrdinal("CustomerID"));
						DateTime orderDate = reader.GetDateTime(reader.GetOrdinal("OrderDate"));
						string customerName = reader.GetString(reader.GetOrdinal("CustomerName"));
						string productName = reader.GetString(reader.GetOrdinal("ProductName"));
						int quantity = reader.GetInt32(reader.GetOrdinal("Quantity"));

						return (customerId, customerName, productName, quantity, orderDate);
					}
				}
			}

			// Bulunamazsa default değer döndürür.
			return (0, string.Empty, string.Empty, 0, DateTime.MinValue);
		}

		/// <summary>
		/// Kabul edilen siparişi loglar.
		/// </summary>
		public static async Task AcceptOrderLogAsync(int orderId, NpgsqlConnection connection)
		{
			var details = await GetOrderDetailsForLogAsync(orderId, connection);
			if (details.CustomerId == 0) return; // Sipariş bulunamadı

			// Log ekleme
			string insertLogQuery = @"
                INSERT INTO Logs (CustomerID, OrderID, LogDate, LogType, LogDetails)
                VALUES (@CustomerID, @OrderID, @LogDate, @LogType, @LogDetails)";

			DateTime logDate = DateTime.Now;
			string logType = "Accept";
			string logDetails = $"{logDate} | {details.CustomerName}'s {details.Quantity} {details.ProductName} order accepted";

			if (connection.State != System.Data.ConnectionState.Open)
			{
				await connection.OpenAsync();
			}

			using (NpgsqlCommand cmdInsert = new NpgsqlCommand(insertLogQuery, connection))
			{
				cmdInsert.Parameters.AddWithValue("@CustomerID", details.CustomerId);
				cmdInsert.Parameters.AddWithValue("@OrderID", orderId);
				cmdInsert.Parameters.AddWithValue("@LogDate", logDate);
				cmdInsert.Parameters.AddWithValue("@LogType", logType);
				cmdInsert.Parameters.AddWithValue("@LogDetails", logDetails);

				await cmdInsert.ExecuteNonQueryAsync();
			}
		}

		public void UpdatePriority()
		{
			// Temel öncelik skoru
			float basePriority = customer.CustomerType == "Premium" ? 15f : 10f;

			// Bekleme süresi: Onaylanmamışsa şu anki zaman - OrderDate
			TimeSpan elapsed = DateTime.Now - OrderDate;
			float waitTimeInSeconds = (float)elapsed.TotalSeconds;
			waitedTime = waitTimeInSeconds;
			// Bekleme süresi ağırlığı
			float waitTimeWeight = 0.5f;

			// Öncelik skoru hesabı
			PriorityScore = basePriority + (waitTimeInSeconds * waitTimeWeight);
		}

		public static async Task<List<Order>> GetProcessingOrdersAsync(NpgsqlConnection connection)
		{
			string query = @"
    SELECT o.OrderID, o.CustomerID, o.ProductID, o.Quantity, o.TotalPrice, o.OrderDate, o.OrderStatus, 
           c.CustomerName, c.CustomerType, p.Price
    FROM Orders o
    JOIN Customers c ON c.CustomerID = o.CustomerID
    JOIN Products p ON p.ProductID = o.ProductID
    WHERE o.OrderStatus = 'Processing'";

			List<Order> processingOrders = new List<Order>();

			if (connection.State != System.Data.ConnectionState.Open)
				await connection.OpenAsync();

			using (var cmd = new NpgsqlCommand(query, connection))
			using (var reader = await cmd.ExecuteReaderAsync())
			{
				while (await reader.ReadAsync())
				{
					int orderId = reader.GetInt32(reader.GetOrdinal("OrderID"));
					int customerId = reader.GetInt32(reader.GetOrdinal("CustomerID"));
					int productId = reader.GetInt32(reader.GetOrdinal("ProductID"));
					int quantity = reader.GetInt32(reader.GetOrdinal("Quantity"));
					DateTime orderDate = reader.GetDateTime(reader.GetOrdinal("OrderDate"));
					decimal price = reader.GetDecimal(reader.GetOrdinal("Price"));

					// Program.orderList'te eşleşen Order'ı bul
					var existingOrder = Program.orderList.FirstOrDefault(o => o.OrderId == orderId);

					if (existingOrder != null)
					{
						// Mevcut Order'ı güncelle
						existingOrder.Quantity = quantity;
						existingOrder.OrderDate = orderDate;
						existingOrder.Price = price;
						processingOrders.Add(existingOrder);
					}
					else
					{
						// Eğer Order yoksa yeni bir Order oluştur
						string customerName = reader.GetString(reader.GetOrdinal("CustomerName"));
						string customerType = reader.GetString(reader.GetOrdinal("CustomerType"));

						Customer customer = new Customer(customerName, 1000, customerType, customerId);
						Order newOrder = new Order(customer, productId, quantity, price)
						{
							OrderId = orderId,
							OrderDate = orderDate
						};

						Program.orderList.Add(newOrder);
						processingOrders.Add(newOrder);
					}
				}
			}

			return processingOrders;
		}



		public async Task LogCancelledOrderAsync(NpgsqlConnection connection, string issue)
		{
			try
			{
				string logInsertQuery = @"
            INSERT INTO Logs (CustomerID, OrderID, LogDate, LogType, LogDetails)
            VALUES (@CustomerID, @OrderID, @LogDate, @LogType, @LogDetails)";

				DateTime logDate = DateTime.Now;
				string logType = "Order Cancelled";
				if (issue != null) { logType = issue; }
				//string logDetails = $"Order #{OrderId} for Product #{ProductId} was cancelled.";

				string logDetails =$"{logDate} | OrderId:#{OrderId}  | {customer.CustomerName}'s {Quantity} {ProductName} order cancelled cause {issue}";
				if (connection.State != System.Data.ConnectionState.Open)
				{
					await connection.OpenAsync();	
				}

				using (NpgsqlCommand cmdInsert = new NpgsqlCommand(logInsertQuery, connection))
				{
					cmdInsert.Parameters.AddWithValue("@CustomerID", CustomerId);
					cmdInsert.Parameters.AddWithValue("@OrderID", OrderId);
					cmdInsert.Parameters.AddWithValue("@LogDate", logDate);
					cmdInsert.Parameters.AddWithValue("@LogType", logType);
					cmdInsert.Parameters.AddWithValue("@LogDetails", logDetails);

					await cmdInsert.ExecuteNonQueryAsync();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error logging cancelled order: {ex.Message}");
			}
		}

		public async Task LogProcessSuccessfulAsync(NpgsqlConnection connection)
		{
			string logInsertQuery = @"
        INSERT INTO Logs (CustomerID, OrderID, LogDate, LogType, LogDetails)
        VALUES (@CustomerID, @OrderID, @LogDate, @LogType, @LogDetails)";

			DateTime logDate = DateTime.Now;
			string logType = "Process Successful";
			string logDetails = $"{logDate} | OrderId:#{OrderId}  | {customer.CustomerName}'s {Quantity} {ProductName} order was successfully processed.";

			if (connection.State != System.Data.ConnectionState.Open)
			{
				await connection.OpenAsync();
			}

			using (NpgsqlCommand cmdInsert = new NpgsqlCommand(logInsertQuery, connection))
			{
				cmdInsert.Parameters.AddWithValue("@CustomerID", CustomerId);
				cmdInsert.Parameters.AddWithValue("@OrderID", OrderId);
				cmdInsert.Parameters.AddWithValue("@LogDate", logDate);
				cmdInsert.Parameters.AddWithValue("@LogType", logType);
				cmdInsert.Parameters.AddWithValue("@LogDetails", logDetails);

				await cmdInsert.ExecuteNonQueryAsync();
			}
		}
	}
}
