using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;
namespace FamilyLife.Domain
{
    public sealed class ProductLot
    {
        public int Id, Thing, Worker, Business, Count, Day, Presentation; public string Origin, Career, Specialty, Kind; public bool Credited, ExperimentFailed;
    }
    /// <summary>Provenance follows stack splits and merges. Uncredited output quantities earn contribution points on deposit or direct delivery; credited fragments never earn twice.</summary>
    public sealed class ProductionBook : IModuleState
    {
        public const string Id="wils.hospitality.production";
        public int NextId=1;
        public readonly List<ProductLot> Lots=new List<ProductLot>();
        public void Move(int from,int to,int count)
        {
            foreach(var lot in Lots.Where(x=>x.Thing==from).ToList())
            {
                int moved=System.Math.Min(count,lot.Count);if(moved<=0)break;
                Lots.Add(new ProductLot{Id=NextId++,Thing=to,Worker=lot.Worker,Business=lot.Business,Count=moved,Day=lot.Day,Origin=lot.Origin,Career=lot.Career,Specialty=lot.Specialty,Kind=lot.Kind,Presentation=lot.Presentation,Credited=lot.Credited,ExperimentFailed=lot.ExperimentFailed});lot.Count-=moved;count-=moved;
            }
            Lots.RemoveAll(x=>x.Count<=0);
        }
        public string Encode()=>new StateWriter().Int("next",NextId).Strings("lots",Lots.Select(x=>new StateWriter().Ints("v",new[]{x.Thing,x.Worker,x.Business,x.Count,x.Day,x.Presentation,x.Id}).Bool("experimentFailed",x.ExperimentFailed).Bool("credited",x.Credited).Strings("key",new[]{x.Origin,x.Career,x.Specialty,x.Kind}).ToString()).ToList()).ToString();
        public static ProductionBook Decode(StateReader r){var b=new ProductionBook{NextId=r.Int("next",1)};foreach(var row in r.Strings("lots")){var s=new StateReader(row);var v=s.Ints("v");var key=s.Strings("key");if(v.Count>=5)b.Lots.Add(new ProductLot{Id=v.Count>6?v[6]:b.NextId++,Thing=v[0],Worker=v[1],Business=v[2],Count=v[3],Day=v[4],Presentation=v.Count>5?v[5]:0,ExperimentFailed=s.Bool("experimentFailed"),Credited=s.Bool("credited"),Origin=key.FirstOrDefault(),Career=key.Count>1?key[1]:null,Specialty=key.Count>2?key[2]:null,Kind=key.Count>3?key[3]:null});}return b;}
    }
    public sealed class ProductionFactory:IStateFactory<ProductionBook>
    {
        public static readonly ProductionFactory Instance=new ProductionFactory();public int Version=>1;public ProductionBook Create()=>new ProductionBook();public ProductionBook Decode(StateReader r,int version)=>ProductionBook.Decode(r);public ProductionBook FromLegacy(StateReader r)=>Create();
    }
    public static class ProductionAccess{public static ProductionBook Production(this FamilyRegistry r)=>r.Shared.Of(ProductionBook.Id,ProductionFactory.Instance);}
}
