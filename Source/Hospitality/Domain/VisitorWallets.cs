using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;
namespace FamilyLife.Domain
{
    public sealed class WalletMember { public int Pawn,Cash; public WalletMember(int pawn,int cash){Pawn=pawn;Cash=cash;} }
    public sealed class WalletPledge { public int Pawn,Silver; }
    public sealed class WalletReservation { public string Key;public int Customer;public readonly List<WalletPledge> Pledges=new List<WalletPledge>();public int Total=>Pledges.Sum(p=>p.Silver); }
    /// <summary>Reservations against outside visitors' physical inventory. No colony account is credited until a real payment arrives.</summary>
    public sealed class VisitorWalletBook:IModuleState
    {
        public const string Id="wils.hospitality.wallets";
        public readonly List<WalletReservation> Reservations=new List<WalletReservation>();
        public int Held(int pawn)=>Reservations.Sum(r=>r.Pledges.Where(p=>p.Pawn==pawn).Sum(p=>p.Silver));
        public int Available(IEnumerable<WalletMember> members)=>members.GroupBy(m=>m.Pawn).Sum(g=>Math.Max(0,g.First().Cash-Held(g.Key)));
        public WalletReservation Find(string key)=>Reservations.FirstOrDefault(r=>r.Key==key);
        public bool Reserve(string key,int customer,IEnumerable<WalletMember> members,int amount)
        {
            if(amount<=0||string.IsNullOrEmpty(key))return false;var old=Find(key);if(old!=null)return old.Customer==customer&&old.Total==amount;
            var reservation=new WalletReservation{Key=key,Customer=customer};int left=amount;
            foreach(var group in members.GroupBy(m=>m.Pawn))
            {
                var member=group.First();int take=Math.Min(left,Math.Max(0,member.Cash-Held(member.Pawn)));if(take>0){reservation.Pledges.Add(new WalletPledge{Pawn=member.Pawn,Silver=take});left-=take;}if(left==0)break;
            }
            if(left>0)return false;Reservations.Add(reservation);return true;
        }
        public bool Replan(string key,int customer,IEnumerable<WalletMember> members,int amount)
        {
            var old=Find(key);if(old!=null)Reservations.Remove(old);
            if(Reserve(key,customer,members,amount))return true;if(old!=null)Reservations.Add(old);return false;
        }
        public void Release(string key){Reservations.RemoveAll(r=>r.Key==key);}
        public string Encode()=>new StateWriter().Strings("holds",Reservations.OrderBy(r=>r.Key,StringComparer.Ordinal).Select(r=>new StateWriter().Strings("key",new[]{r.Key}).Int("customer",r.Customer).Ints("p",r.Pledges.Select(p=>p.Pawn).ToList()).Ints("v",r.Pledges.Select(p=>p.Silver).ToList()).ToString()).ToList()).ToString();
        public static VisitorWalletBook Decode(StateReader r)
        {
            var b=new VisitorWalletBook();foreach(var row in r.Strings("holds")){var x=new StateReader(row);var p=x.Ints("p");var v=x.Ints("v");string key=x.Strings("key").FirstOrDefault();if(key==null||b.Find(key)!=null)continue;var h=new WalletReservation{Key=key,Customer=x.Int("customer")};for(int i=0;i<Math.Min(p.Count,v.Count);i++)if(v[i]>0)h.Pledges.Add(new WalletPledge{Pawn=p[i],Silver=v[i]});b.Reservations.Add(h);}return b;
        }
    }
    public sealed class VisitorWalletFactory:IStateFactory<VisitorWalletBook>{public static readonly VisitorWalletFactory Instance=new VisitorWalletFactory();public int Version=>1;public VisitorWalletBook Create()=>new VisitorWalletBook();public VisitorWalletBook Decode(StateReader r,int version)=>VisitorWalletBook.Decode(r);public VisitorWalletBook FromLegacy(StateReader r)=>Create();}
    public static class VisitorWalletAccess {public static VisitorWalletBook VisitorWallets(this FamilyRegistry r)=>r.Shared.Of(VisitorWalletBook.Id,VisitorWalletFactory.Instance);}
}
