using Npgsql;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System;
using System.Threading.Tasks;
using System.Data;

namespace threadProject
{
	public class WorkerThread
	{
		private Thread _thread;
		private bool _isRunning = false;

		// Atanan siparişi tutacağımız kuyruk
		private static readonly object _orderQueueLock = new object();
		private static Queue<Order> _orderQueue = new Queue<Order>();

		// Thread'in boşta olup olmadığını anlamak için
		public bool IsBusy { get; private set; } = false;

		public WorkerThread()
		{
			_thread = new Thread(new ThreadStart(StartWorker));
		}

		public void Start()
		{
			_isRunning = true;
			_thread.Start();
		}

		public void Stop()
		{
			_isRunning = false;
		}


		/// 
		/// Dışarıdan yeni sipariş ekleme
		///
		public void EnqueueOrder(Order order)
		{
			lock (_orderQueueLock)
			{
				_orderQueue.Enqueue(order);
			}
		}

		/// 
		/// Thread’in çalışma döngüsü.
		/// 
		private async void StartWorker()
		{
			while (_isRunning)
			{
				Order nextOrder = null;

				// Kuyruktan sipariş çek
				lock (_orderQueueLock)
				{
					if (_orderQueue.Count > 0)
					{
						nextOrder = _orderQueue.Dequeue();
						IsBusy = true;  // Sipariş aldık, artık meşgul
					}
				}

				// İşleyecek sipariş yoksa bekle
				if (nextOrder == null)
				{
					IsBusy = false;
					Thread.Sleep(200);
					continue;
				}

				// Siparişi işleme alma kısmı  
				//	ProcessSingleOrder(nextOrder).Wait(); önemli unutma


				var processTask = ProcessSingleOrder(nextOrder);

				// 15 saniyeyi aşarsa Timeout
				bool finishedInTime = processTask.Wait(15000);

				if (!finishedInTime)  //timeout 
				{
					// 15 sn içinde tamamlanmadı -> Veritabanında siparişi "Cancelled" yap ve "timeout" logu ekle
					try
					{
						using (var conn = new NpgsqlConnection(Program.connectionString))
						{
							conn.Open();

							// Order durumunu Cancelled yap
							Order.UpdateOrderStatusAsync(nextOrder.OrderId, "Cancelled", conn).Wait();

							// Timeout olarak logla
							nextOrder.LogCancelledOrderAsync(conn, "timeout").Wait();

							// Program tarafına "iptal edildi" sinyali
							GlobalEvents.RaiseOrderCancelled(nextOrder).Wait();
						}
					}
					catch (Exception ex)
					{
						// Timeout loglaması sırasında da bir hata olursa gösterebilirsiniz
						MessageBox.Show($"Timeout sonrası iptal/loglama hatası: {ex.Message}");
					}
				}




				// Sipariş işlenince meşguliyet bitsin
				IsBusy = false;

				// Bir sipariş tamamlandıktan sonra Program tarafına "bitti" sinyali verelim (örnek event)
				GlobalEvents.OnOrderCompleted();
			}
		}

		/// 
		/// içindeki gibi stok/bütçe kontrolü, iptal vs. mantığıyla işler.
		/// 
		private async Task ProcessSingleOrder(Order order)
		{

			await Program.dbSemaphore.WaitAsync();
			string cs = Program.connectionString;
			try
			{

				using (var conn = new NpgsqlConnection(cs))
				{
					await conn.OpenAsync();
					await Order.UpdateOrderStatusAsync(order.OrderId, "Critical Process", conn);
					GlobalEvents.isCriticalSection();
					// Sorgu string’leri:
					string productCheckQuery = "SELECT COUNT(*) FROM Products WHERE ProductID = @ProductID";
					string stockCheckQuery = "SELECT Stock FROM Products WHERE ProductID = @ProductID";
					string updateStockQuery = "UPDATE Products SET Stock = Stock - @Quantity WHERE ProductID = @ProductID";
					string updateCustomerQuery = @"
                UPDATE Customers
                SET Budget = Budget - @TotalPrice,
                    TotalSpent = TotalSpent + @TotalPrice
                WHERE CustomerID = @CustomerID";
					string updateTypeQuery = "UPDATE Customers SET CustomerType = 'Premium' WHERE CustomerID = @CustomerID";

					// 1) Ürün var mı kontrolü (COUNT(*))
					using (NpgsqlCommand cmd = new NpgsqlCommand(productCheckQuery, conn))
					{
						cmd.Parameters.AddWithValue("@ProductID", order.ProductId);
						int productCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

						if (productCount == 0)
						{
							await Order.UpdateOrderStatusAsync(order.OrderId, "Cancelled", conn);
							await order.LogCancelledOrderAsync(conn, "not exist");
							await GlobalEvents.RaiseOrderCancelled(order);
							return;
						}
					}

					// 2) Stok kontrolü (SELECT Stock)
					int productStock;
					using (NpgsqlCommand cmd = new NpgsqlCommand(stockCheckQuery, conn))
					{
						cmd.Parameters.AddWithValue("@ProductID", order.ProductId);
						productStock = Convert.ToInt32(await cmd.ExecuteScalarAsync());
					}

					if (order.Quantity > productStock)
					{
						await Order.UpdateOrderStatusAsync(order.OrderId, "Cancelled", conn);
						await order.LogCancelledOrderAsync(conn, "low stock");
						await GlobalEvents.RaiseOrderCancelled(order);
						return;
					}

					// 3) Bütçe kontrolü (Bellekte)
					if (order.customer.Budget < order.TotalPrice)
					{
						await Order.UpdateOrderStatusAsync(order.OrderId, "Cancelled", conn);
						await order.LogCancelledOrderAsync(conn, "low balance");
						await GlobalEvents.RaiseOrderCancelled(order);
						return;
					}

					//limit sayı kontrolu

					/*string sumQuantityQuery = @"
    SELECT COALESCE(SUM(quantity), 0)
        FROM orders
        WHERE CustomerID = @CustomerID 
          AND ProductID = @ProductID";
						using (NpgsqlCommand cmd = new NpgsqlCommand(sumQuantityQuery, conn))
						{
							cmd.Parameters.AddWithValue("@CustomerID", order.CustomerId);
							cmd.Parameters.AddWithValue("@ProductID", order.ProductId);

							// Veritabanından, ilgili müşteri ve ürünle ilgili tüm kayıtlardaki toplam Quantity değerini al
							int existingQuantity = Convert.ToInt32(await cmd.ExecuteScalarAsync());

							// Mevcut (toplam) miktara, yeni siparişin miktarını ekle
						   //int totalQuantity = existingQuantity + order.Quantity;

							// 5'ten büyük mü kontrol et
							if (existingQuantity + order.Quantity > 5)
							{
								// İptal
								await Order.UpdateOrderStatusAsync(order.OrderId, "Cancelled", conn);
								await order.LogCancelledOrderAsync(conn, $"tq= {existingQuantity + order.Quantity}"); //exceeded limit(5)
								await GlobalEvents.RaiseOrderCancelled(order);
								return;
							}
						}*/
					// Satış işlemi

					//  Stok düşürme 
					using (NpgsqlCommand cmd = new NpgsqlCommand(updateStockQuery, conn))
					{
						cmd.Parameters.AddWithValue("@Quantity", order.Quantity);
						cmd.Parameters.AddWithValue("@ProductID", order.ProductId);
						await cmd.ExecuteNonQueryAsync();
					}

					//Müşteri harcama güncelleme 
					using (NpgsqlCommand cmd = new NpgsqlCommand(updateCustomerQuery, conn))
					{
						cmd.Parameters.AddWithValue("@TotalPrice", order.TotalPrice);
						cmd.Parameters.AddWithValue("@CustomerID", order.CustomerId);
						await cmd.ExecuteNonQueryAsync();
					}

					// Müşteri nesnesini güncelle
					order.customer.Budget -= order.TotalPrice;
					order.customer.TotalSpent += order.TotalPrice;

					//Premium’a yükseltme kontrolü
					if (order.customer.TotalSpent >= 2000)
					{
						using (NpgsqlCommand cmd = new NpgsqlCommand(updateTypeQuery, conn))
						{
							cmd.Parameters.AddWithValue("@CustomerID", order.customer.CustomerId);
							await cmd.ExecuteNonQueryAsync();
						}

						// Program.customersList’te de güncelle
						var cInList = Program.customersList.FirstOrDefault(x => x.CustomerId == order.customer.CustomerId);
						if (cInList != null)
						{
							cInList.CustomerType = "Premium";
							cInList.Budget = order.customer.Budget;
							cInList.TotalSpent = order.customer.TotalSpent;
						}
					}
					else
					{
						// Program.customersList’te budget ve totalSpent güncelle
						var cInList = Program.customersList.FirstOrDefault(x => x.CustomerId == order.customer.CustomerId);
						if (cInList != null)
						{
							cInList.Budget = order.customer.Budget;
							cInList.TotalSpent = order.customer.TotalSpent;
						}
					}

					// 6Sipariş Durumu
					await Order.UpdateOrderStatusAsync(order.OrderId, "Process Successful", conn);
					await order.LogProcessSuccessfulAsync(conn);
					await GlobalEvents.RaiseProcessSuccessful(order);

					// Müşteri Panelini güncelle UI Thread
					GlobalEvents.OnCustomerPanelReloadRequired();

				//	GlobalEvents.OnOrderCompleted();
				//  await Task.Delay(30000);

				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error in ProcessSingleOrder: {ex.Message}");
			}
			finally
			{
				Program.dbSemaphore.Release();
			}
		}

	}
}
