﻿using Geek.Server.Message.DemoBag;
using System.Threading.Tasks;

namespace Geek.Server.Demo
{
    public class DemoBagCompAgent : StateComponentAgent<DemoBagComp, DemoBagState>
    {
        public Task Init()
        {
            //赠送默认道具
            if(State.ItemMap.Count <= 0)
            {
                State.ItemMap.Add(101, 1);
                State.ItemMap.Add(103, 100);
            }
            return Task.CompletedTask;
        }

        public Task<long> GetItemNum(int itemId)
        {
            long num = 0;
            if (State.ItemMap.ContainsKey(itemId))
                num = State.ItemMap[itemId];
            return Task.FromResult(num);
        }

        public Task AddItem(int itemId, long num)
        {
            if (State.ItemMap.ContainsKey(itemId))
                State.ItemMap[itemId] += num;
            else
                State.ItemMap[itemId] = num;
            return Task.CompletedTask;
        }

        public Task CutItem(int itemId, long num)
        {
            if (State.ItemMap.ContainsKey(itemId))
            {
                State.ItemMap[itemId] -= num;
                if (State.ItemMap[itemId] <= 0)
                    State.ItemMap.Remove(itemId);
            }
            return Task.CompletedTask;
        }

        public Task<ResBagInfo> BuildInfoMsg()
        {
            var res = new ResBagInfo();
            foreach (var kv in State.ItemMap)
                res.itemDic[kv.Key] = kv.Value;
            return Task.FromResult(res);
        }
    }
}
