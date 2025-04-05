using Npgsql;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace threadProject
{
	public partial class MainForm : Form
	{
		public List<Customer> customersList;
		public MainForm(List<Customer> customers)
		{
			InitializeComponent();
			customersList = customers;
			string projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\"));
			string backgroundPath = Path.Combine(projectDir, "images", "nvyBlue.jpg");
			this.BackgroundImage = Image.FromFile(backgroundPath);
			this.BackgroundImageLayout = ImageLayout.Stretch;

			GlobalEvents.OrderApproved += async () =>
			{
				// Onaylanan siparişler işleme alınır
				await processViewMethod();
			};

			GlobalEvents.ProcessSuccessful += async (order) =>
			{
				if (this.InvokeRequired)
				{
					// UI thread'ine dönmek için
					this.Invoke(new Action(async () =>
					{
						await processViewMethod();
					}));
				}
				else
				{
					// Zaten UI thread'indeysek doğrudan çağır
					await processViewMethod();
				}
			};

			GlobalEvents.OrderCancelled += async (order) =>
			{
				if (this.InvokeRequired)
				{
					// UI thread'ine dönmek için
					this.Invoke(new Action(async () =>
					{
						await processViewMethod();
					}));
				}
				else
				{
					// Zaten UI thread'indeysek doğrudan çağır
					await processViewMethod();
				}
			};

			GlobalEvents.inCriticalSection += async ()=>
			{
				if (this.InvokeRequired)
				{
					// UI thread'ine dönmek için
					this.Invoke(new Action(async () =>
					{
						await processViewMethod();
					}));
				}
				else
				{
					// Zaten UI thread'indeysek doğrudan çağır
					await processViewMethod();
				}
			};

			GlobalEvents.PriorityListChanged += async (List<Order> priorityOrders) =>
			{
				// UI thread'inde değilsek, Invoke ile UI thread’ine dön
				if (this.InvokeRequired)
				{
					this.Invoke(new Action(() =>
					{
						UpdateListBox(priorityOrders);
					}));
				}
				else
				{
					UpdateListBox(priorityOrders);
				}
			};

		}

		private async Task processViewMethod()
		{
			// 1) Datayı OrderId'ye göre küçükten büyüğe çek
			string query = @"
        SELECT OrderID, ProductID, CustomerID, Quantity, orderstatus
        FROM Orders
        ORDER BY OrderID ASC
    ";

			DataTable dt = new DataTable();
			using (var conn = new NpgsqlConnection(Program.connectionString))
			{
				await conn.OpenAsync();
				using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
				using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
				{
					dt.Load(reader);
				}
			}


			// ====================================
			// 2) DataGridView'e bağlamadan önce, mevcut scroll pozisyonunu (dikey + yatay) saklama kısmı
			// ====================================
			int savedFirstDisplayedRowIndex = 0;
			int savedHorizontalOffset = 0;

			if (processDGView.RowCount > 0)
			{
				try
				{
					savedFirstDisplayedRowIndex = processDGView.FirstDisplayedScrollingRowIndex;
					savedHorizontalOffset = processDGView.HorizontalScrollingOffset;
				}
				catch
				{
					
				}
			}

			processDGView.Visible = true;
			processDGView.AutoGenerateColumns = true;
			processDGView.DataSource = dt;


			if (dt.Rows.Count > 0)
			{
				if (savedFirstDisplayedRowIndex >= dt.Rows.Count)
					savedFirstDisplayedRowIndex = dt.Rows.Count - 1;
				if (savedFirstDisplayedRowIndex < 0)
					savedFirstDisplayedRowIndex = 0;

				try
				{
					processDGView.FirstDisplayedScrollingRowIndex = savedFirstDisplayedRowIndex;
					processDGView.HorizontalScrollingOffset = savedHorizontalOffset;
				}
				catch
				{
				}
			}


			// 3) "State" adında ekstra bir kolon ekle
			if (!processDGView.Columns.Contains("State"))
			{
				var stateColumn = new DataGridViewTextBoxColumn();
				stateColumn.Name = "State";
				stateColumn.HeaderText = "ProgressState";
				processDGView.Columns.Add(stateColumn);
			}

			// 4) "State" kolonundaki hücreleri ProgressBar gibi boyamak için CellPainting eventi
			processDGView.CellPainting -= processDGView_CellPainting; // varsa önce unsubscribe
			processDGView.CellPainting += processDGView_CellPainting;

			processDGView.Refresh();
		}

		private void processDGView_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
		{
			Color color;
			// Sadece DataGridView'de "State" kolonuna ait hücreler için özel çizim yap
			if (e.RowIndex >= 0 &&
				e.ColumnIndex == processDGView.Columns["State"].Index)
			{
				// 1) Hücrenin arka planını normal çizelim (hücre borders vs.)
				e.PaintBackground(e.CellBounds, true);

				// 2) İlgili satırdaki "Status" değerine göre bir progress değeri hesaplayalım.
				//    Elinizde bir "Status" veya benzeri kolon varsa, oradaki değerlere göre
				//    progressValue belirleyebilirsiniz. Örnek olarak:
				string statusValue = processDGView.Rows[e.RowIndex].Cells["orderstatus"].Value?.ToString() ?? "";
				int progressValue = 0;

				// Basit bir örnek: 
				// "Running" => %50
				// "Process Successful" => %100
				// Diğer durumlarda => %0
				switch (statusValue)
				{
					case "Running":
						progressValue = 25;
						color = Color.Gold;
						break;

					case "Critical Process":
						progressValue = 50;
						color = Color.Blue;
						break;

					case "Process Successful":
						progressValue = 100;
						color = Color.Green;
						break;
					default:
						progressValue = 0;
						color = Color.Red;
						break;
				}

				// 3) ProgressBar (dolu dikdörtgen) çizimi
				//    Hücre kenarlarını biraz daraltalım:
				Rectangle barBounds = e.CellBounds;
				barBounds.Inflate(-2, -2);

				// Dolu kısım
				int filledWidth = (int)(barBounds.Width * progressValue / 100f);
				Rectangle fillRect = new Rectangle(barBounds.X, barBounds.Y, filledWidth, barBounds.Height);

				// Örnek renkleri seçelim
				using (Brush brush = new SolidBrush(color))
				{
					e.Graphics.FillRectangle(brush, fillRect);
				}
				

				// 5) Event'i biz işlediğimizi belirt
				e.Handled = true;
			}
		}


		private void loginPanelBtn_Click(object sender, EventArgs e)
		{
			if (panel2.Visible == false)
			{
				panel2.Visible = true;
			}
			else
			{
				panel2.Visible = false;
			}
		}

		private void button1_Click(object sender, EventArgs e)
		{
			try
			{
				if (textBox1.Text == "admin" && textBox2.Text == "1")
				{
					// admin login succsess
					Program.createAdminPanel();
				}
				else
				{
					foreach (Customer customer in customersList)
					{
						if ((customer.username == Convert.ToInt16(textBox1.Text)) && (customer.password == Convert.ToInt16(textBox2.Text)))
						{
							// customer login succsess
							Program.createCustomerPanel(customer);

						}
					}
				}
				label4.Visible = false;
			}
			catch (Exception ex)
			{
				label4.Visible = true;
			}
		}
		private async void UpdateListBox(List<Order> priorityOrders)
		{
			listBox1.Items.Clear();
			foreach (var order in priorityOrders)
			{
				listBox1.Items.Add(
					$"Order ID : {order.OrderId} | Priority: {order.PriorityScore} | Wait Time: {order.waitedTime}"
				);
				listBox1.Refresh();

			}
		}



		private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
		{
			Program.closeProgram();
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			Program.closeProgram();
		}
	}
}
