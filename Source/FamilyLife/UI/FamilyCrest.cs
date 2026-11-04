using FamilyLife.Domain;
using UnityEngine;
using Verse;
namespace FamilyLife
{
    /// <summary>Six shared monochrome masks; colors and backgrounds are drawn directly. No per-lineage texture cache.</summary>
    internal static class FamilyCrest
    {
        private static readonly Texture2D[] masks = new Texture2D[CrestStyle.Marks];
        private static readonly Color[] colors = { new Color(.55f,.12f,.12f),new Color(.12f,.25f,.55f),new Color(.15f,.4f,.2f),new Color(.48f,.3f,.1f),new Color(.35f,.18f,.5f),new Color(.15f,.15f,.17f),new Color(.12f,.4f,.4f),new Color(.9f,.85f,.65f) };
        internal static void Draw(Rect rect,Lineage lineage)
        {
            int primary=CrestStyle.Index(lineage.CrestPrimary,lineage.Id,CrestStyle.Colors);
            int secondary=CrestStyle.Index(lineage.CrestSecondary,7,CrestStyle.Colors);
            int mark=CrestStyle.Index(lineage.CrestMark,lineage.Id/CrestStyle.Colors,CrestStyle.Marks);
            int pattern=CrestStyle.Index(lineage.CrestPattern,0,CrestStyle.Patterns);
            Widgets.DrawBoxSolid(rect,colors[primary]);
            if(pattern==1) Widgets.DrawBoxSolid(new Rect(rect.x+rect.width/2,rect.y,rect.width/2,rect.height),colors[secondary]);
            if(pattern==2)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,rect.width/2,rect.height/2),colors[secondary]);
                Widgets.DrawBoxSolid(new Rect(rect.x+rect.width/2,rect.y+rect.height/2,rect.width/2,rect.height/2),colors[secondary]);
            }
            float side=Mathf.Min(rect.width,rect.height)*.65f;
            var icon=new Rect(rect.center.x-side/2,rect.center.y-side/2,side,side);
            Widgets.DrawBoxSolid(icon,colors[primary]);
            if(masks[mark]==null) masks[mark]=MakeMask(mark);
            var previous=GUI.color;GUI.color=primary==7?Color.black:Color.white;
            GUI.DrawTexture(icon,masks[mark]);GUI.color=previous;
            Widgets.DrawBox(rect,1);
        }
        private static Texture2D MakeMask(int mark)
        {
            var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
            var pixels=new Color[32*32];
            for(int y=0;y<32;y++) for(int x=0;x<32;x++)
            {
                float dx=x-15.5f,dy=y-15.5f,r=dx*dx+dy*dy;
                bool filled=mark==0 ? r<36 || (Mathf.Abs(dx)<1.5f || Mathf.Abs(dy)<1.5f) && r<150 :
                    mark==1 ? Mathf.Abs(dx)+Mathf.Abs(dy)<13 && Mathf.Abs(dx)+Mathf.Abs(dy)>8 :
                    mark==2 ? Mathf.Abs(dy-Mathf.Abs(dx)*.65f+4)<2 && Mathf.Abs(dx)<12 :
                    mark==3 ? (Mathf.Abs(dx)<2 || Mathf.Abs(dy)<2) && Mathf.Abs(dx)<12 && Mathf.Abs(dy)<12 :
                    mark==4 ? r>60 && r<130 :
                    (Mathf.Abs(dx+8)<2 || Mathf.Abs(dx)<2 || Mathf.Abs(dx-8)<2) && Mathf.Abs(dy)<11;
                pixels[y*32+x]=filled?Color.white:Color.clear;
            }
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
    }
}
