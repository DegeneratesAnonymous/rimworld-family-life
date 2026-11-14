using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    public enum EstablishmentKind { Restaurant, Tavern, Inn, Shop }
    public enum WorkshopFocus { General, Smithy, Tailor, Armory, Mechanist, Boutique }
    public enum EstablishmentOwner { Community, Person, Household }
    public enum ServiceStage { Ordered, Delivering, Delivered, Consumed, Settled, Cancelled }
    public enum ContributionKind { Production, Service, Checkout, Support }

    public sealed class Establishment
    {
        public int Id, Map, OwnerId, Counter, PricePercent = 100, RoomPrice = 8, BasePayMilli = 250, StaffShare = 20;
        public string Name = "New establishment";
        public EstablishmentKind Kind;
        public WorkshopFocus WorkshopFocus;
        public EstablishmentOwner Owner;
        public VenuePersonality Personality=new VenuePersonality();
        public bool Enabled, Residents = true, Visitors = true, Alcohol = true, CutOffDrunks = true, Quiet;
        public int OpenHour = 8, CloseHour = 22, MaxStay = 5, HouseholdBudget = 15, Reserve = 20;
        public readonly HashSet<int> BannedCustomers=new HashSet<int>(),ExcludedFamilies=new HashSet<int>();
        public readonly List<int> Staff = new List<int>(), Benches = new List<int>(), Beds = new List<int>();
        public readonly HashSet<int> ServiceCells = new HashSet<int>(), StockCells = new HashSet<int>(), StorageCells = new HashSet<int>();
        public readonly HashSet<int> StockContainers = new HashSet<int>(), StorageContainers = new HashSet<int>();
        public int Reliability = 50, Food = 50, Service = 50, Comfort = 50, Value = 50, Safety = 50, Rowdiness;
        public int Visits, Revenue, BonusPool, BonusRemainder, LastPayday = -1, LastReviewDay = -1;
        public readonly Dictionary<int, int> Points = new Dictionary<int, int>();
        public readonly int[] Weights = { 4, 3, 2, 1 };
        public bool OpenAt(int hour) => Enabled && OpenHour != CloseHour && (OpenHour < CloseHour ? hour >= OpenHour && hour < CloseHour : hour >= OpenHour || hour < CloseHour);
        public int Reputation => (Reliability + Food + Service + Comfort + Value + Safety) / 6;
        public int Weight(ContributionKind kind) => Math.Max(0, Weights[(int)kind]);
        public void Review(int food, int service, int comfort, int value, int safety, int rowdy)
        {
            Food = Smooth(Food, food); Service = Smooth(Service, service); Comfort = Smooth(Comfort, comfort); Value = Smooth(Value, value); Safety = Smooth(Safety, safety);
            Rowdiness = Smooth(Rowdiness, rowdy); Reliability = Smooth(Reliability, (service + comfort) / 2); Visits++;
        }
        private static int Smooth(int old, int experience) => Math.Max(0, Math.Min(100, (int)Math.Round(old * .9 + experience * .1)));
    }
    public sealed class ServiceOrder
    {
        public int Id, Business, Customer, Thing, Count = 1, Price, Created, DeliveredTick, Worker, TableCell, LastTouched;
        public string Account, ExternalKey;
        public bool Visitor, Lodging, Refunded, TipAttempted, DietRequest;
        public int Tip;
        public int Booking, Maker, Presentation, AllowanceDay=-1;
        public string AllowanceAccount;
        public ServiceStage Stage;
        public string Key => "establishment-order:" + Id;
    }
    public sealed class StaffEarning
    {
        public int Id, Business, Worker, Silver, DueDay;
        public bool Posted;
    }
    public sealed class GuestStay
    {
        public int Pawn, Business, Group, ArrivalDay, UntilDay, Bed, Extensions, LastDecisionDay = -1;
        public bool Generated, Leaving;
        public int LastLodgingDay = -1, SleepTicks;
        public float PersonalExperience = .5f;
    }
    public sealed class EstablishmentBook : IModuleState
    {
        public const string StateId = "wils.hospitality.establishments";
        public int NextId = 1, NextOrder = 1, NextEarning = 1, NextVisitorDay = 2;
        public readonly List<Establishment> Businesses = new List<Establishment>();
        public readonly List<ServiceOrder> Orders = new List<ServiceOrder>();
        public readonly List<StaffEarning> Earnings = new List<StaffEarning>();
        public readonly List<GuestStay> Guests = new List<GuestStay>();
        public readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Fractions = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Memories = new Dictionary<string, int>();
        public readonly Dictionary<string, int> HouseholdSpent = new Dictionary<string, int>();
        public readonly List<string> History = new List<string>();
        public Establishment Create(int map, EstablishmentKind kind) { var e = new Establishment { Id = NextId++, Map = map, Kind = kind, Name = "New " + kind.ToString().ToLowerInvariant() }; Businesses.Add(e); return e; }
        public Establishment ById(int id) => Businesses.FirstOrDefault(e => e.Id == id);
        public ServiceOrder Order(int id) => Orders.FirstOrDefault(o => o.Id == id);
        public bool Once(string key, int day) { if (Seen.ContainsKey(key)) return false; Seen[key] = day; return true; }
        public void Note(string text) { History.Add(text); while (History.Count > 100) History.RemoveAt(0); }
        public bool Memory(int who, string key, int other, int day, int cooldown = 3)
        {
            string id = who + ":" + key + ":" + other;
            if (Memories.TryGetValue(id, out int last) && day - last < cooldown) return false;
            Memories[id] = day; return true;
        }
        public bool Credit(Establishment e, int worker, ContributionKind kind, string key, int day, int units = 1)
        {
            if (worker <= 0 || units <= 0 || !Once("contribution:" + key + ":" + worker + ":" + kind, day)) return false;
            e.Points[worker] = (e.Points.TryGetValue(worker, out int n) ? n : 0) + e.Weight(kind) * (kind == ContributionKind.Production ? units : Math.Min(units, 4)); return true;
        }
        public void WorkTime(Establishment e, int worker, int ticks, string sampleKey, int day)
        {
            if (ticks <= 0 || !int.TryParse(sampleKey.Substring(sampleKey.LastIndexOf(':') + 1), out int through)) return;
            string sample = "last-time:" + worker;
            if (Fractions.TryGetValue(sample, out int previous)) ticks = Math.Min(ticks, through - previous);
            if (ticks <= 0) return;
            Fractions[sample] = through;
            string key = "time:" + e.Id + ":" + worker;
            long accrued = (Fractions.TryGetValue(key, out int old) ? old : 0) + (long)Math.Max(0, e.BasePayMilli) * ticks;
            // 2500 ticks per hour; milli-silver carries so short shifts do not round up.
            int silver = (int)(accrued / 2500000); Fractions[key] = (int)(accrued % 2500000);
            Earn(e, worker, silver, BusinessCalendar.Next(day));
        }
        public void Earn(Establishment e, int worker, int silver, int due)
        {
            if (silver <= 0) return;
            // Aggregate by worker/payday; immutable after posting to the bank.
            var old = Earnings.FirstOrDefault(x => !x.Posted && x.Business == e.Id && x.Worker == worker && x.DueDay == due);
            if (old != null) old.Silver += silver;
            else Earnings.Add(new StaffEarning { Id = NextEarning++, Business = e.Id, Worker = worker, Silver = silver, DueDay = due });
        }
        public void Sale(Establishment e, int amount)
        {
            e.Revenue += amount; int share = Math.Max(0, Math.Min(80, e.StaffShare));
            int milli = amount * share + e.BonusRemainder; e.BonusPool += milli / 100; e.BonusRemainder = milli % 100;
        }
        public ServiceOrder Begin(Establishment e, int customer, int thing, int price, int tick, string account, bool visitor, ICommerceBank bank, int count = 1, EstablishmentBooking booking = null, BookingBook bookings = null)
        {
            if (price <= 0 || Orders.Any(o => o.Customer == customer && o.Stage < ServiceStage.Settled)) return null;
            var order = new ServiceOrder { Id = NextOrder++, Business = e.Id, Customer = customer, Thing = thing, Price = price, Created = tick, LastTouched = tick, Account = booking?.Sponsor ?? account, Visitor = booking == null && visitor, Count = count, Booking = booking?.Id ?? 0 };
            if (booking != null) { if (bookings == null || !bookings.ReserveOrder(booking,order.Key,price,bank)) return null; }
            else if (!visitor && (bank == null || !bank.Reserve(order.Key, account, price))) return null;
            Orders.Add(order); return order;
        }
        public bool Checkout(ServiceOrder order, ICommerceBank bank, int day, Func<ServiceOrder, bool> externalPayment)
        {
            if (order.Stage == ServiceStage.Settled) return true;
            if (order.Stage != ServiceStage.Consumed) return false;
            var e = ById(order.Business); if (e == null) return false;
            if (order.Visitor ? externalPayment == null || !externalPayment(order) : bank == null || !bank.Commit(order.Key, bank.Business(e.Id), day)) return false;
            order.Stage = ServiceStage.Settled; Sale(e, order.Price); return true;
        }
        public bool GiveTip(ServiceOrder order,int worker,int silver,int day,ICommerceBank bank,Func<bool> physicalPayment=null)
        {
            if(order.Stage!=ServiceStage.Settled||order.TipAttempted||bank==null||worker<=0)return false;order.TipAttempted=true;
            if(silver<=0||silver>3)return false;string key="tip:"+order.Key;var e=ById(order.Business);if(e==null)return false;
            bool paid=order.Visitor?bank.ExternalSale(key,bank.Business(e.Id),silver,day,physicalPayment):bank.Transfer(key,bank.Person(order.Customer),bank.Business(e.Id),silver,day,true);
            if(!paid)return false;Earn(e,worker,silver,BusinessCalendar.Next(day));order.Tip=silver;return true;
        }
        public bool Cancel(ServiceOrder order, ICommerceBank bank, BookingBook bookings=null)
        {
            if (order.Stage >= ServiceStage.Delivered && order.Stage <= ServiceStage.Consumed) return false;
            if (order.Stage >= ServiceStage.Settled) return true;
            if(order.Booking>0){if(bookings==null||!bookings.CancelOrder(order,bank))return false;}
            else if(!order.Visitor){if(bank==null)return false;bank.Cancel(order.Key);}
            if(order.AllowanceAccount!=null){string k=order.AllowanceAccount+":"+order.AllowanceDay;if(HouseholdSpent.TryGetValue(k,out int spent))HouseholdSpent[k]=Math.Max(0,spent-order.Price);}
            order.Stage = ServiceStage.Cancelled; return true;
        }
        public int Withdrawable(Establishment e, ICommerceBank bank) => bank == null ? 0 : Math.Max(0, bank.Available(bank.Business(e.Id)) - bank.Owed(bank.Business(e.Id)) - Earnings.Where(x => x.Business == e.Id && !x.Posted).Sum(x => x.Silver) - e.BonusPool - e.Reserve);
        public void Payday(Establishment e, ICommerceBank bank, int day)
        {
            if (bank == null) return;
            int payday = BusinessCalendar.Latest(day); if (e.LastPayday >= payday) return;
            if (e.BonusPool > 0 && e.Points.Values.Sum() > 0)
            {
                long total = e.Points.Values.Sum(v => (long)v); int pool = e.BonusPool;
                var shares = e.Points.ToDictionary(x => x.Key, x => (int)(pool * (long)x.Value / total));
                int remainder = pool - shares.Values.Sum();
                foreach (var x in e.Points.OrderByDescending(x => pool * (long)x.Value % total).ThenBy(x => x.Key).Take(remainder)) shares[x.Key]++;
                foreach (var x in shares) Earn(e, x.Key, x.Value, payday);
                e.BonusPool = 0; e.Points.Clear();
            }
            foreach (var x in Earnings.Where(x => x.Business == e.Id && !x.Posted && x.DueDay <= payday))
                x.Posted = bank.Wages("hospitality:" + x.Id, bank.Business(e.Id), x.Worker, x.Silver, x.DueDay);
            if (Earnings.Any(x => x.Business == e.Id && !x.Posted && x.DueDay <= payday)) return;
            bank.Payday(bank.Business(e.Id), payday); e.LastPayday = payday;
            Earnings.RemoveAll(x => x.Posted); // the bank now owns the obligation and its lifetime
        }
        public void Prune(int day, int tick = -1)
        {
            foreach (var k in Seen.Where(k => day - k.Value > 60).Select(k => k.Key).ToList()) Seen.Remove(k);
            foreach (var k in Memories.Where(k => day - k.Value > 30).Select(k => k.Key).ToList()) Memories.Remove(k);
            foreach (var k in HouseholdSpent.Keys.Where(k => !k.EndsWith(":" + day)).ToList()) HouseholdSpent.Remove(k);
            Orders.RemoveAll(o => o.Stage >= ServiceStage.Settled && (tick >= 0 ? tick - o.Created > 180000 : day - o.Created / 60000 > 3));
        }
        public string Encode()
        {
            var w = new StateWriter().Int("next", NextId).Int("order", NextOrder).Int("earning", NextEarning).Int("visitor", NextVisitorDay);
            w.Strings("businesses", Businesses.Select(EncodeBusiness).ToList());
            w.Strings("orders", Orders.Select(o => new StateWriter().Ints("v", new[] { o.Id,o.Business,o.Customer,o.Thing,o.Count,o.Price,o.Created,o.DeliveredTick,o.Worker,o.TableCell,(int)o.Stage,o.LastTouched,o.Booking,o.Maker,o.Presentation,o.AllowanceDay,o.Tip }).Strings("a", new[] { o.Account ?? "", o.ExternalKey ?? "",o.AllowanceAccount??"" }).Bool("visitor",o.Visitor).Bool("lodging",o.Lodging).Bool("refunded",o.Refunded).Bool("diet",o.DietRequest).Bool("tip",o.TipAttempted).ToString()).ToList());
            w.Strings("earnings", Earnings.Select(e => new StateWriter().Ints("v", new[] { e.Id,e.Business,e.Worker,e.Silver,e.DueDay }).Bool("posted",e.Posted).ToString()).ToList());
            w.Strings("guests", Guests.Select(g => new StateWriter().Ints("v", new[] { g.Pawn,g.Business,g.Group,g.ArrivalDay,g.UntilDay,g.Bed,g.Extensions,g.LastDecisionDay,g.LastLodgingDay,g.SleepTicks }).Bool("generated",g.Generated).Bool("leaving",g.Leaving).Float("experience",g.PersonalExperience).ToString()).ToList());
            WriteMap(w,"seen",Seen); WriteMap(w,"fractions",Fractions); WriteMap(w,"memory",Memories); WriteMap(w,"budget",HouseholdSpent);
            return w.Strings("history",History).ToString();
        }
        private static string EncodeBusiness(Establishment e)
        {
            var w = new StateWriter().Strings("name",new[] { e.Name }).Ints("v", new[] { e.Id,e.Map,e.OwnerId,e.Counter,e.PricePercent,e.RoomPrice,e.BasePayMilli,e.StaffShare,(int)e.Kind,(int)e.Owner,e.OpenHour,e.CloseHour,e.MaxStay,e.HouseholdBudget,e.Reserve,e.Reliability,e.Food,e.Service,e.Comfort,e.Value,e.Safety,e.Rowdiness,e.Visits,e.Revenue,e.BonusPool,e.LastPayday,e.LastReviewDay,e.BonusRemainder,(int)e.WorkshopFocus });
            w.Bool("enabled",e.Enabled).Bool("residents",e.Residents).Bool("visitors",e.Visitors).Bool("alcohol",e.Alcohol).Bool("cutoff",e.CutOffDrunks).Bool("quiet",e.Quiet);
            w.Ints("staff",e.Staff).Ints("benches",e.Benches).Ints("beds",e.Beds).Ints("service",e.ServiceCells.OrderBy(x=>x).ToList()).Ints("stock",e.StockCells.OrderBy(x=>x).ToList()).Ints("storage",e.StorageCells.OrderBy(x=>x).ToList()).Ints("stockcontainers",e.StockContainers.OrderBy(x=>x).ToList()).Ints("storagecontainers",e.StorageContainers.OrderBy(x=>x).ToList()).Ints("weights",e.Weights);
            w.Ints("banned",e.BannedCustomers.OrderBy(x=>x).ToList()).Ints("excluded",e.ExcludedFamilies.OrderBy(x=>x).ToList());
            w.Strings("personality",new[]{e.Personality.Encode()});
            WriteMap(w,"points",e.Points.ToDictionary(x=>x.Key.ToString(),x=>x.Value)); return w.ToString();
        }
        private static void WriteMap(StateWriter w,string key,Dictionary<string,int> map) { w.Strings(key,map.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new StateWriter().Strings("k",new[]{x.Key}).Int("v",x.Value).ToString()).ToList()); }
        private static void ReadMap(StateReader r,string key,Dictionary<string,int> map) { foreach(var s in r.Strings(key)) { var x=new StateReader(s); var k=x.Strings("k").FirstOrDefault(); if(k!=null) map[k]=x.Int("v"); } }
        public static EstablishmentBook Decode(StateReader r)
        {
            var b = new EstablishmentBook { NextId=r.Int("next",1),NextOrder=r.Int("order",1),NextEarning=r.Int("earning",1),NextVisitorDay=r.Int("visitor",2) };
            foreach(var row in r.Strings("businesses"))
            {
                var x=new StateReader(row); var v=x.Ints("v"); if(v.Count<28) continue;
                var e=new Establishment { Id=v[0],Map=v[1],OwnerId=v[2],Counter=v[3],PricePercent=v[4],RoomPrice=v[5],BasePayMilli=v[6],StaffShare=v[7],Kind=(EstablishmentKind)v[8],Owner=(EstablishmentOwner)v[9],OpenHour=v[10],CloseHour=v[11],MaxStay=v[12],HouseholdBudget=v[13],Reserve=v[14],Reliability=v[15],Food=v[16],Service=v[17],Comfort=v[18],Value=v[19],Safety=v[20],Rowdiness=v[21],Visits=v[22],Revenue=v[23],BonusPool=v[24],LastPayday=v[25],LastReviewDay=v[26],BonusRemainder=v[27],WorkshopFocus=v.Count>28?(WorkshopFocus)v[28]:WorkshopFocus.General,Name=x.Strings("name").FirstOrDefault()??"Establishment",Enabled=x.Bool("enabled"),Residents=x.Bool("residents"),Visitors=x.Bool("visitors"),Alcohol=x.Bool("alcohol"),CutOffDrunks=x.Bool("cutoff"),Quiet=x.Bool("quiet") };
                e.Personality=VenuePersonality.Decode(new StateReader(x.Strings("personality").FirstOrDefault()??""));
                e.BannedCustomers.UnionWith(x.Ints("banned"));e.ExcludedFamilies.UnionWith(x.Ints("excluded"));e.Staff.AddRange(x.Ints("staff"));e.Benches.AddRange(x.Ints("benches"));e.Beds.AddRange(x.Ints("beds"));e.ServiceCells.UnionWith(x.Ints("service"));e.StockCells.UnionWith(x.Ints("stock"));e.StorageCells.UnionWith(x.Ints("storage"));e.StockContainers.UnionWith(x.Ints("stockcontainers"));e.StorageContainers.UnionWith(x.Ints("storagecontainers"));var weights=x.Ints("weights");for(int i=0;i<Math.Min(4,weights.Count);i++)e.Weights[i]=weights[i];
                var points=new Dictionary<string,int>();ReadMap(x,"points",points);foreach(var p in points)if(int.TryParse(p.Key,out int id))e.Points[id]=p.Value;b.Businesses.Add(e);
            }
            foreach(var row in r.Strings("orders")) {var x=new StateReader(row);var v=x.Ints("v");var a=x.Strings("a");if(v.Count<12)continue;b.Orders.Add(new ServiceOrder{Id=v[0],Business=v[1],Customer=v[2],Thing=v[3],Count=v[4],Price=v[5],Created=v[6],DeliveredTick=v[7],Worker=v[8],TableCell=v[9],Stage=(ServiceStage)v[10],LastTouched=v[11],Booking=v.Count>12?v[12]:0,Maker=v.Count>13?v[13]:0,Presentation=v.Count>14?v[14]:0,AllowanceDay=v.Count>15?v[15]:-1,AllowanceAccount=a.ElementAtOrDefault(2),Account=a.ElementAtOrDefault(0),ExternalKey=a.ElementAtOrDefault(1),Visitor=x.Bool("visitor"),Lodging=x.Bool("lodging"),Refunded=x.Bool("refunded"),DietRequest=x.Bool("diet"),TipAttempted=x.Bool("tip"),Tip=v.Count>16?v[16]:0});}
            foreach(var row in r.Strings("earnings")){var x=new StateReader(row);var v=x.Ints("v");if(v.Count>=5)b.Earnings.Add(new StaffEarning{Id=v[0],Business=v[1],Worker=v[2],Silver=v[3],DueDay=v[4],Posted=x.Bool("posted")});}
            foreach(var row in r.Strings("guests")){var x=new StateReader(row);var v=x.Ints("v");if(v.Count>=9)b.Guests.Add(new GuestStay{Pawn=v[0],Business=v[1],Group=v[2],ArrivalDay=v[3],UntilDay=v[4],Bed=v[5],Extensions=v[6],LastDecisionDay=v[7],LastLodgingDay=v[8],SleepTicks=v.Count>9?v[9]:0,Generated=x.Bool("generated"),Leaving=x.Bool("leaving"),PersonalExperience=x.Float("experience",.5f)});}
            ReadMap(r,"seen",b.Seen);ReadMap(r,"fractions",b.Fractions);ReadMap(r,"memory",b.Memories);ReadMap(r,"budget",b.HouseholdSpent);b.History.AddRange(r.Strings("history").Take(100));return b;
        }
    }
    public sealed class EstablishmentFactory : IStateFactory<EstablishmentBook>
    {
        public static readonly EstablishmentFactory Instance=new EstablishmentFactory();public int Version=>1;
        public EstablishmentBook Create()=>new EstablishmentBook();public EstablishmentBook Decode(StateReader r,int version)=>EstablishmentBook.Decode(r);public EstablishmentBook FromLegacy(StateReader r)=>Create();
    }
    public static class EstablishmentAccess { public static EstablishmentBook Establishments(this FamilyRegistry r)=>r.Shared.Of(EstablishmentBook.StateId,EstablishmentFactory.Instance); }
}
