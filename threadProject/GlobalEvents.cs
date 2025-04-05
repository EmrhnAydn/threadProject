using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace threadProject
{
	public static class GlobalEvents
	{
		public static event Action ProductListChanged;
		public static void OnProductListChanged()
		{
			ProductListChanged?.Invoke();
		}

		public static event Action LogAdded;
		public static void OnLogAdded()
		{
			LogAdded?.Invoke();
		}

		public static event Action OrderAdded;
		public static void OnOrderAdded()
		{
			OrderAdded?.Invoke();
		}


		public static event Action OrderApproved;
		public static void OnOrderApproved()
		{
			OrderApproved?.Invoke();
		}


		// Critical section event for trigger the process animation xD
		public static event Action inCriticalSection;
		public static void isCriticalSection()
		{
			inCriticalSection?.Invoke();
		}







		public static event Func<Order, Task> OrderCancelled;
		public static async Task RaiseOrderCancelled(Order order)
		{
			if (OrderCancelled != null)
			{
				await OrderCancelled.Invoke(order);
			}
		}

		public static event Func<Order, Task> ProcessSuccessful;
		public static async Task RaiseProcessSuccessful(Order order)
		{
			if (ProcessSuccessful != null)
			{
				await ProcessSuccessful.Invoke(order);
			}
		}






		public static event Action CustomerPanelReloadRequired;
		public static void OnCustomerPanelReloadRequired()
		{
			CustomerPanelReloadRequired?.Invoke();
		}



		public static event Func<Task> OrderCompleted;

		public static async Task OnOrderCompleted()
		{
			if (OrderCompleted != null)
			{
				await OrderCompleted.Invoke();
			}
		}





		// priotery
		public static event Func<List<Order>, Task> PriorityListChanged;

		public static async Task UpdatePriorityList(List<Order> newPriority)
		{
			if (PriorityListChanged != null)
			{
				await PriorityListChanged(newPriority);
			}
		}


	}
}