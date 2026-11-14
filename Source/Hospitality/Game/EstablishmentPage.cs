using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    public static class EstablishmentPage
    {
        private static int selected;
        private static string rename;
        public static float Draw(Rect rect,FamilyComponent c)
        {
            var book=c.Registry.Establishments();float y=rect.y;float width=rect.width;
            Action<string> label=text=>{Widgets.Label(new Rect(rect.x,y,width,28),text);y+=30;};
            Func<string,bool> button=text=>{bool clicked=Widgets.ButtonText(new Rect(rect.x,y,width,28),text);y+=32;return clicked;};
            label("Establishments — restaurants, taverns, inns and shops");
            if(button("Create establishment")) Menu(Enum.GetValues(typeof(EstablishmentKind)).Cast<EstablishmentKind>().Select(kind=>new FloatMenuOption(kind.ToString(),()=>{var e=book.Create(Find.CurrentMap.uniqueID,kind);selected=e.Id;rename=e.Name;})));
            foreach(var e in book.Businesses)
            {
                var bank=FamilyFramework.Commerce.Bank;string coverage=HospitalityService.Coverage(e).Count()+" working";
                if(button(e.Name+" · "+(HospitalityService.Map(e)!=null&&e.OpenAt(GenLocalDate.HourOfDay(HospitalityService.Map(e)))?"Open":"Closed")+" · "+coverage+" · reputation "+e.Reputation)) {selected=e.Id;rename=e.Name;}
            }
            var current=book.ById(selected);if(current==null)return y-rect.y;
            var b=current;
            Widgets.DrawLineHorizontal(rect.x,y,width);y+=10;
            rename=Widgets.TextField(new Rect(rect.x,y,width,28),rename??b.Name);b.Name=rename.Length>60?rename.Substring(0,60):rename;y+=32;
            Check(rect,ref y,"Open for business",ref b.Enabled);
            label("Owner: "+OwnerName(b,c));
            if(button("Change owner"))
            {
                var options=new List<FloatMenuOption>{new FloatMenuOption("Community",()=>{b.Owner=EstablishmentOwner.Community;b.OwnerId=0;})};
                foreach(var p in c.Registry.Persons.Where(p=>p.InColony&&!p.Juvenile)) {var who=p;options.Add(new FloatMenuOption(who.Name,()=>{b.Owner=EstablishmentOwner.Person;b.OwnerId=who.Id;}));}
                foreach(var h in c.Registry.Households.Where(h=>h.ResidentIds.Any(id=>c.Registry.GetPerson(id)?.InColony==true))) {var house=h;options.Add(new FloatMenuOption("Household: "+string.Join(", ",house.ResidentIds.Select(id=>c.Registry.GetPerson(id)?.Name).Where(n=>n!=null)),()=>{b.Owner=EstablishmentOwner.Household;b.OwnerId=house.Id;}));}
                Menu(options);
            }
            label("Staff: "+string.Join(", ",b.Staff.Select(id=>c.TryGetPawn(id)?.LabelShort??("#"+id))));
            if(button("Assign / remove staff")) Menu(c.Registry.Persons.Where(p=>p.InColony&&!p.Juvenile).Select(p=>new FloatMenuOption((b.Staff.Contains(p.Id)?"Remove ":"Assign ")+p.Name,()=>{if(!b.Staff.Remove(p.Id)){b.Staff.Add(p.Id);if(b.Kind!=EstablishmentKind.Shop)c.Registry.Assignments().Assign(p.Id,CareerService.Professions.ById(CulinaryCareers.Cook),c.Registry.Careers(),FrameworkServices.Now);}})));
            label("Staff use personal Work hours and Cooking / Sell priorities. Uncovered service pauses for necessary breaks.");
            foreach(var entry in new[]{new[]{"Service area","Service"},new[]{"Sales stock area","Stock"},new[]{"Private storage area","Storage"},new[]{"Sales containers","StockContainer"},new[]{"Storage containers","StorageContainer"},new[]{"Counter","Counter"},new[]{"Kitchen benches","Bench"}})
            {
                string mode=entry[1];if(button("Designate "+entry[0])) Activate(b,mode);
            }
            if(b.Kind==EstablishmentKind.Inn&&button("Designate guest beds"))Activate(b,"Bed");
            label("Service: "+b.ServiceCells.Count+" cells · sales: "+b.StockCells.Count+" cells / "+b.StockContainers.Count+" containers · storage: "+b.StorageCells.Count+" cells / "+b.StorageContainers.Count+" containers");
            label("Counter: "+b.Counter+" · benches: "+b.Benches.Count+" · guest beds: "+b.Beds.Count);
            if(button("Erase designated cells / unselect objects")) Menu(new[]{"Service","Stock","Storage","StockContainer","StorageContainer","Counter","Bench","Bed"}.Select(mode=>new FloatMenuOption(mode,()=>Activate(b,mode,true))));
            if(button("Ban / allow individual customer"))Menu(c.Registry.Persons.Where(p=>p.InColony||book.Guests.Any(g=>g.Pawn==p.Id)).Select(p=>new FloatMenuOption((b.BannedCustomers.Contains(p.Id)?"Allow ":"Ban ")+p.Name,()=>{if(!b.BannedCustomers.Remove(p.Id))b.BannedCustomers.Add(p.Id); })));
            if(button("Exclude / allow family"))Menu(c.Registry.Persons.Where(p=>p.PrimaryLineageId>0).GroupBy(p=>p.PrimaryLineageId).Select(g=>new FloatMenuOption((b.ExcludedFamilies.Contains(g.Key)?"Allow ":"Exclude ")+g.First().Name+" family",()=>{if(!b.ExcludedFamilies.Remove(g.Key))b.ExcludedFamilies.Add(g.Key); })));
            label("Customers");Check(rect,ref y,"Residents",ref b.Residents);Check(rect,ref y,"Visitors",ref b.Visitors);Check(rect,ref y,"Alcohol service (respects customer restrictions)",ref b.Alcohol);Check(rect,ref y,"Refuse further alcohol to intoxicated customers",ref b.CutOffDrunks);Check(rect,ref y,"Quiet house rules",ref b.Quiet);
            Slider(rect,ref y,"Opening hour",ref b.OpenHour,0,23);Slider(rect,ref y,"Closing hour",ref b.CloseHour,0,23);
            label("Equal opening and closing hours means closed; overnight schedules are supported.");
            Slider(rect,ref y,"Prices (% market value)",ref b.PricePercent,25,250);Slider(rect,ref y,"Lodging per night (silver)",ref b.RoomPrice,1,50);Slider(rect,ref y,"Maximum guest stay (days)",ref b.MaxStay,1,10);Slider(rect,ref y,"Household spending allowance / day",ref b.HouseholdBudget,0,100);
            Slider(rect,ref y,"Base wage (milli-silver / work hour)",ref b.BasePayMilli,0,2000);Slider(rect,ref y,"Staff share of collected sales (%)",ref b.StaffShare,0,80);
            for(int i=0;i<4;i++){int value=b.Weights[i];Slider(rect,ref y,((ContributionKind)i)+" credit weight",ref value,0,10);b.Weights[i]=value;}
            Slider(rect,ref y,"Business reserve (silver)",ref b.Reserve,0,500);
            if(button("Suggested pay from owner's personality"))HospitalityService.OwnerPreset(b);
            var finance=FamilyFramework.Commerce.Bank;
            if(finance==null) label("Treasury unavailable: paid orders and new paid shifts paused; saved obligations retained.");
            else
            {
                string account=finance.Business(b.Id);label("Business spendable: "+finance.Available(account)+" · wage debt: "+finance.Owed(account)+" · pending base pay: "+book.Earnings.Where(e=>e.Business==b.Id&&!e.Posted).Sum(e=>e.Silver)+" · staff pool: "+b.BonusPool);
                if(button("Fund business: 25 silver from owner"))HospitalityService.Fund(b,25);
                if(button("Withdraw available profit: "+book.Withdrawable(b,finance)))HospitalityService.Withdraw(b);
            }
            foreach(var adapter in OptionalCommerceHooks.Status)label("Compatibility: "+adapter);
            label("Character: "+b.Personality.Description);
            label("Warmth "+b.Personality.Culture.Warmth+" · refinement "+b.Personality.Culture.Refinement+" · thrift "+b.Personality.Culture.Frugality+" · discipline "+b.Personality.Culture.Discipline+" · revelry "+b.Personality.Culture.Revelry);
            label("Staff climate: "+b.Personality.StaffClimate+"/100 · "+EstablishmentPersonalityService.OwnerMood(b));
            Check(rect,ref y,"Allow autonomous owner conversations",ref b.Personality.OwnerAutonomy);
            Check(rect,ref y,"Allow discretionary owner bonuses from free business funds",ref b.Personality.AllowBonuses);
            Check(rect,ref y,"Require equal treatment (prevent family favoritism)",ref b.Personality.EqualTreatment);
            label(b.Personality.LastReason);
            label("Character develops gradually from owners, staff and recent customers. A bad mood changes conversations, not earned wages or your policy settings.");
            label("Reputation: food "+b.Food+" · service "+b.Service+" · comfort "+b.Comfort+" · value "+b.Value+" · safety "+b.Safety);
            label("Atmosphere: "+(b.Rowdiness>60?"Rowdy":b.Rowdiness<30?"Peaceful":"Sociable")+" · completed visits "+b.Visits+" · collected sales "+b.Revenue);
            label("Recovery: improve supplies, seating, coverage and value; enforce alcohol / quiet rules. New experiences gradually outweigh older ones.");
            foreach(var guest in book.Guests.Where(g=>g.Business==b.Id))label((HospitalityService.Pawn(guest.Pawn)?.LabelShort??"Guest")+": until day "+guest.UntilDay+", "+guest.Extensions+" extensions"+(guest.Leaving?"; leaving":""));
            if(button("Reserve a family meal / celebration (50 silver budget)")) Menu(c.Registry.Households.Where(h=>h.ResidentIds.Any(id=>c.Registry.GetPerson(id)?.InColony==true)).Select(h=>new FloatMenuOption("Household #"+h.Id,()=>HospitalityStories.BookMeal(b,BookingKind.Celebration,h.Id,h.ResidentIds.Count,50))));
            if(button("Commission a community meal (100 silver budget)"))HospitalityStories.BookMeal(b,BookingKind.CommunityMeal,0,c.Registry.Persons.Count(p=>p.InColony),100);
            if(b.Kind==EstablishmentKind.Inn&&button("Offer emergency meals / shelter (100 silver community budget)"))HospitalityStories.BookMeal(b,BookingKind.EmergencyShelter,0,c.Registry.Persons.Count(p=>p.InColony),100);
            label("Bookings reserve the sponsor's actual funds; unused funds are released when the booking expires. Customers still choose whether to attend.");
            foreach(var booking in c.Registry.HospitalityBookings().Bookings.Where(x=>x.Business==b.Id&&!x.Closed))label(booking.Kind+": "+booking.Served+" served; "+(booking.Budget-booking.Committed)+" silver budget remaining");
            if(button("Resolve a recent bill with a full refund"))Menu(book.Orders.Where(o=>o.Business==b.Id&&o.Stage==ServiceStage.Settled&&!o.Refunded).Select(o=>new FloatMenuOption((HospitalityService.Pawn(o.Customer)?.LabelShort??"Customer #"+o.Customer)+": "+o.Price+" silver",()=>HospitalityStories.Refund(o))));
            label("Recent activity");foreach(var line in book.History.AsEnumerable().Reverse().Take(15))label(line);
            return y-rect.y;
        }
        private static string OwnerName(Establishment b,FamilyComponent c)=>b.Owner==EstablishmentOwner.Community?"Community":b.Owner==EstablishmentOwner.Person?c.Registry.GetPerson(b.OwnerId)?.Name??"Former owner":"Household #"+b.OwnerId;
        private static void Menu(IEnumerable<FloatMenuOption> options)=>Find.WindowStack.Add(new FloatMenu(options.ToList()));
        private static void Activate(Establishment b,string mode,bool erase=false){Find.MainTabsRoot.SetCurrentTab(null);Find.DesignatorManager.Select(new EstablishmentDesignator(b.Id,mode,erase));}
        private static void Check(Rect rect,ref float y,string text,ref bool value){Widgets.CheckboxLabeled(new Rect(rect.x,y,rect.width,26),text,ref value);y+=28;}
        private static void Slider(Rect rect,ref float y,string text,ref int value,int min,int max){Widgets.Label(new Rect(rect.x,y,rect.width,24),text+": "+value);y+=24;value=(int)Math.Round(Widgets.HorizontalSlider(new Rect(rect.x,y,rect.width,22),value,min,max,true));y+=28;}
    }
    public sealed class EstablishmentDesignator : Designator_Cells
    {
        private readonly int business;private readonly string mode;private readonly bool erase;
        private Zone_Stockpile paintedZone;
        private int DraggableDimensions=>mode=="Service"||mode=="Stock"||mode=="Storage"?2:0;
        public EstablishmentDesignator(int id,string mode,bool erase=false){business=id;this.mode=mode;this.erase=erase;defaultLabel=(erase?"Remove ":"Designate ")+mode;defaultDesc="Select premises for this establishment. Designated stock is explicitly allocated from communal supplies; private kitchen bills use only business stock/storage.";icon=ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Stockpile",false)??BaseContent.BadTex;useMouseIcon=true;}
        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            var e=FamilyComponent.Current?.Registry.Establishments().ById(business);if(e==null||Map.uniqueID!=e.Map||!cell.InBounds(Map)||cell.Fogged(Map))return false;
            if(DraggableDimensions==2)
            {
                if(mode!="Service"&&!erase&&FamilyComponent.Current.Registry.Establishments().Businesses.Any(other=>other.Id!=e.Id&&other.Map==e.Map&&(other.StockCells.Contains(Map.cellIndices.CellToIndex(cell))||other.StorageCells.Contains(Map.cellIndices.CellToIndex(cell)))))return "Another establishment already owns this stock area.";
                return true;
            }
            return Pick(cell)!=null;
        }
        private Thing Pick(IntVec3 cell)=>cell.GetThingList(Map).FirstOrDefault(t=>mode=="Counter"?(t.def.surfaceType==SurfaceType.Eat||HospitalityInterop.IsRegister(t)):mode=="Bed"?t is Building_Bed:mode=="Bench"?t is IBillGiver:t is IStoreSettingsParent);
        public override void DesignateSingleCell(IntVec3 cell)
        {
            var e=FamilyComponent.Current?.Registry.Establishments().ById(business);if(e==null)return;
            int index=Map.cellIndices.CellToIndex(cell);
            if(DraggableDimensions==2){var cells=mode=="Service"?e.ServiceCells:mode=="Stock"?e.StockCells:e.StorageCells;if(erase){if(mode!="Service"&&cells.Contains(index))HospitalityNarrative.SuppliesRemoved(e,cell);cells.Remove(index);}else
                {
                    cells.Add(index);
                    if(mode!="Service"&&Map.zoneManager.ZoneAt(cell)==null&&cell.Standable(Map))
                    {
                        if(paintedZone==null){paintedZone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,Map.zoneManager);paintedZone.label=e.Name+" "+mode.ToLowerInvariant();Map.zoneManager.RegisterZone(paintedZone);}
                        paintedZone.AddCell(cell);
                    }
                }
                return;}
            var t=Pick(cell);if(t==null)return;
            var others=FamilyComponent.Current.Registry.Establishments().Businesses.Where(o=>o.Id!=e.Id&&o.Map==e.Map);
            if(!erase&&others.Any(o=>o.Counter==t.thingIDNumber||o.Beds.Contains(t.thingIDNumber)||o.Benches.Contains(t.thingIDNumber)||o.StockContainers.Contains(t.thingIDNumber)||o.StorageContainers.Contains(t.thingIDNumber))){Messages.Message("That object belongs to another establishment.",MessageTypeDefOf.RejectInput,false);return;}
            if(mode=="Counter"&&!erase&&!HospitalityInterop.CanUseCounter(t)){Messages.Message("Close the native Gastronomy restaurant and let its diners finish before assigning its register to this establishment. One system must own service.",MessageTypeDefOf.RejectInput,false);return;}
            if(mode=="Counter")e.Counter=erase?0:t.thingIDNumber;
            else if(mode=="Bed"||mode=="Bench"){var ids=mode=="Bed"?e.Beds:e.Benches;if(erase)ids.Remove(t.thingIDNumber);else if(!ids.Contains(t.thingIDNumber))ids.Add(t.thingIDNumber);}
            else{var ids=mode=="StockContainer"?e.StockContainers:e.StorageContainers;if(erase){if(ids.Contains(t.thingIDNumber))HospitalityNarrative.SuppliesRemoved(e,cell);ids.Remove(t.thingIDNumber);}else ids.Add(t.thingIDNumber);}
        }
        public override void SelectedUpdate(){base.SelectedUpdate();var e=FamilyComponent.Current?.Registry.Establishments().ById(business);if(e!=null&&e.Map==Map.uniqueID)GenDraw.DrawFieldEdges(e.ServiceCells.Select(Map.cellIndices.IndexToCell).ToList());}
    }
}
