using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;
namespace FamilyLife.Domain
{
    public enum BookingKind { FamilyMeal, Celebration, CommunityMeal, EmergencyShelter }
    public sealed class EstablishmentBooking
    {
        public int Id,Business,Household,Start,End,Budget,Committed,Served,Expected;public bool Closed;
        public BookingKind Kind;public string Sponsor;
        public string Key=>"hospitality-booking:"+Id;
    }
    public sealed class BookingBook:IModuleState
    {
        public const string Id="wils.hospitality.bookings";public int Next=1;public readonly List<EstablishmentBooking> Bookings=new List<EstablishmentBooking>();
        public EstablishmentBooking Create(Establishment e,BookingKind kind,int household,int expected,int budget,string sponsor,int tick,ICommerceBank bank)
        {
            if(bank==null||budget<=0||expected<=0)return null;
            var booking=new EstablishmentBooking{Id=Next++,Business=e.Id,Household=household,Expected=expected,Budget=budget,Sponsor=sponsor,Start=tick,End=tick+60000,Kind=kind};
            if(!bank.Reserve(booking.Key,sponsor,budget))return null;Bookings.Add(booking);return booking;
        }
        public EstablishmentBooking For(int business,int household,int now)=>Bookings.FirstOrDefault(b=>b.Business==business&&!b.Closed&&now>=b.Start&&now<b.End&&(b.Household==0||b.Household==household)&&b.Committed<b.Budget);
        public bool ReserveOrder(EstablishmentBooking b,string orderKey,int price,ICommerceBank bank)
        {
            if(b.Closed||price<=0||b.Committed+price>b.Budget)return false;
            if(!bank.SplitReservation(b.Key,orderKey,price))return false;b.Committed+=price;return true;
        }
        public void Close(EstablishmentBooking b,ICommerceBank bank){bank?.Cancel(b.Key);b.Closed=true;}
        public string Encode()=>new StateWriter().Int("next",Next).Strings("bookings",Bookings.Select(b=>new StateWriter().Ints("v",new[]{b.Id,b.Business,b.Household,b.Start,b.End,b.Budget,b.Committed,b.Served,b.Expected,(int)b.Kind}).Bool("closed",b.Closed).Strings("s",new[]{b.Sponsor}).ToString()).ToList()).ToString();
        public static BookingBook Decode(StateReader r){var book=new BookingBook{Next=r.Int("next",1)};foreach(var row in r.Strings("bookings")){var x=new StateReader(row);var v=x.Ints("v");if(v.Count<10)continue;book.Bookings.Add(new EstablishmentBooking{Id=v[0],Business=v[1],Household=v[2],Start=v[3],End=v[4],Budget=v[5],Committed=v[6],Served=v[7],Expected=v[8],Kind=(BookingKind)v[9],Closed=x.Bool("closed"),Sponsor=x.Strings("s").FirstOrDefault()});}return book;}
    }
    public sealed class BookingFactory:IStateFactory<BookingBook>{public static readonly BookingFactory Instance=new BookingFactory();public int Version=>1;public BookingBook Create()=>new BookingBook();public BookingBook Decode(StateReader r,int v)=>BookingBook.Decode(r);public BookingBook FromLegacy(StateReader r)=>Create();}
    public static class BookingAccess{public static BookingBook HospitalityBookings(this FamilyRegistry r)=>r.Shared.Of(BookingBook.Id,BookingFactory.Instance);}
}
