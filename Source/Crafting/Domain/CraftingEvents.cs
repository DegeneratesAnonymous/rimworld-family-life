using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public sealed class CraftingEvent
    {
        public string Id,Role,Fact,Thought;public int Mood,Opinion;
        public bool Matches(string role,string fact,bool other)=>Role==role&&Fact==fact&&(Opinion==0||other);
    }
    public static class CraftingEvents
    {
        public static readonly string[] Roles={"Woodworker","Stoneworker","Smith","Tailor","Weaponsmith","Armorsmith","Mechanist","Couturier","Outfitter"};
        private static readonly string[] Positive={"FineWork","DifficultOrder","MaterialMastery","CustomerDelight","ProudCraft"};
        private static readonly string[] Negative={"FlawedWork","WastedMaterial","RushedWork","RejectedOrder","WorkshopFailure"};
        private static readonly string[] Social={"CraftPraised","HelpfulAdvice","SharedTechnique","CreativeDispute","StolenCredit"};
        public static readonly CraftingEvent[] All=Build().ToArray();
        private static IEnumerable<CraftingEvent> Build()
        {
            foreach(var role in Roles)
            {
                for(int i=0;i<5;i++)yield return new CraftingEvent{Id=role+"Positive"+i,Role=role,Fact=Positive[i],Thought="WFL_C_Positive"+(i+1),Mood=2+i%2};
                for(int i=0;i<5;i++)yield return new CraftingEvent{Id=role+"Negative"+i,Role=role,Fact=Negative[i],Thought="WFL_C_Negative"+(i+1),Mood=-2-i%2};
                for(int i=0;i<5;i++)yield return new CraftingEvent{Id=role+"Social"+i,Role=role,Fact=Social[i],Thought="WFL_C_Social"+(i+1),Opinion=i<3?3:-3};
            }
        }
        public static IEnumerable<CraftingEvent> Eligible(string role,string fact,bool other)=>All.Where(e=>e.Matches(role,fact,other));
    }
}
