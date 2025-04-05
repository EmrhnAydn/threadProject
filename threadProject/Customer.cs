using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace threadProject
{
	public class Customer
	{
		public int CustomerId { get; set; }
		public int username { get; set; }
		public int password { get; set; }
		public string CustomerName { get; set; }
		public decimal Budget { get; set; } = 0;
		public string CustomerType { get; set; }
		public decimal TotalSpent { get; set; } = 0; 

		public float WaitTime { get; set; } = 0; // Bekleme süresi
		public float PriorityScore { get; set; } = 0; // öncelik sırası 
		public DataTable dt;
		public List<Order> orders;

		public Customer(string customerName, decimal budget, string customerType, int i)
		{
			CustomerName = customerName;
			Budget = budget;
			CustomerType = customerType;
			username = i + 1;
			password = i + 1;

		}

		public void UpdatePriority()
		{
			if (dt != null && dt.Rows.Count > 0 && dt.Columns.Contains("orderdate"))
			{
				DateTime orderDate = Convert.ToDateTime(dt.Rows[0]["OrderDate"]);
				float calculatedWaitTime = (float)(DateTime.Now - orderDate).TotalMilliseconds;
				WaitTime = calculatedWaitTime;
			}
			else
			{
				WaitTime = 0f;
			}

			float basePriority = CustomerType == "Premium" ? 15f : 10f;
			PriorityScore = basePriority + (WaitTime * 5f);
		}

	}
}
