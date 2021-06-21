/**
 * Auto generated, do not edit it server
 *
 * 用于测试的道具表
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NLog;

namespace Geek.Server.Config
{
	public class t_itemContainer : BaseContainer
	{
	    private static readonly NLog.Logger LOGGER = LogManager.GetCurrentClassLogger();

		private List<t_itemBean> list = new List<t_itemBean>();
		private Dictionary<int, t_itemBean> map = new Dictionary<int, t_itemBean>();

		//public override List<t_itemBean> getList()
		public override IList getList()
		{
			return list;
		}

		//public override Dictionary<int, t_itemBean> getMap()
		public override IDictionary getMap()
		{
			return map;
		}
		
		public Type BinType = typeof(t_itemBean);

		public override void loadDataFromBin()
		{    
			map.Clear();
			list.Clear();
			Loaded = true;
			
			string binPath = System.Environment.CurrentDirectory + "/Bytes/t_itemBean.bytes";
            byte[] data;
            if (File.Exists(binPath))
                data = File.ReadAllBytes(binPath);
            else
            	throw new Exception("can not find " + binPath);
			// FieldCount:int + FieldType:byte(0:int 1:long 2:string 3:float)
			int offset = 87;  
			while (data.Length > offset)
			{
				t_itemBean bean = new t_itemBean();
				bean.LoadData(data, ref offset);
				list.Add(bean);
				if(!map.ContainsKey(bean.t_id))
					map.Add(bean.t_id, bean);
				else
					throw new Exception("Exist duplicate Key: " + bean.t_id + " t_itemBean");
			}
		}
		
	}
}


