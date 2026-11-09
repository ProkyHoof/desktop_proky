using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class WardrobeTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception("Wardrobe: "+message);}
    static object Field(object value,string name){return value.GetType().GetField(name,Private).GetValue(value);}
    static void Invoke(object value,string name,params object[] args){value.GetType().GetMethod(name,Private).Invoke(value,args);}
    static PetWardrobeWindow Open(KeyboardPetWindow pet)
    {
        var menu=(PetContextMenu)Field(pet,"menu");
        Check(!menu.Items.Commands().Any(item=>item.Text=="选择发型"),"hairstyle entry is merged into wardrobe");
        menu.Items.Commands().Single(item=>item.Text=="皮肤…").PerformClick();
        Application.DoEvents();return (PetWardrobeWindow)Field(pet,"wardrobeWindow");
    }
    internal static void Run(string root,string testRoot,string previewPath)
    {
        Directory.CreateDirectory(testRoot);
        string data=Path.Combine(testRoot,"user-data"),config=Path.Combine(data,"keyboard_pet_config.json");
        Directory.CreateDirectory(data);
        File.WriteAllText(config,"{\"hairStyle\":0,\"wearHair\":3,\"wearBody\":2,\"wearHands\":1,\"follow\":0}");
        using(var pet=new KeyboardPetWindow(root,false,dataRoot:data))
        {
            ((Timer)Field(pet,"timer")).Stop();
            var original=pet.scene;pet.inputState.Key(65,true,0);
            var window=Open(pet);
            Check(window.Visible&&!window.Modal&&pet.Enabled,"wardrobe is modeless");
            Check(Object.ReferenceEquals(window,Open(pet)),"repeated entry reuses the window");
            string before=File.ReadAllText(config);
            window.SelectHairStyle(PetHairStyles.Reference);
            var scene=(BoundPetScene)Field(window,"scene");
            Check(scene.Portrait.HairStyle==PetHairStyles.Reference&&pet.scene.Portrait.HairStyle==PetHairStyles.Short,"choice updates preview only");
            Check(File.ReadAllText(config)==before,"preview leaves saved configuration intact");
            window.Close();
            Check(pet.scene.Portrait.HairStyle==PetHairStyles.Short,"closing discards unapplied choice");
            window=Open(pet);scene=(BoundPetScene)Field(window,"scene");window.SelectHairStyle(PetHairStyles.Reference);window.ApplySelection();
            Check(pet.scene==original&&pet.inputState.HasKeys&&pet.scene.Portrait.HairStyle==1,"apply preserves scene and held input");
            Check(File.ReadAllText(config).Contains("\"hairStyle\":1")&&!File.ReadAllText(config).Contains("wear"),"save retains hairstyle and drops retired prototype fields");
            Invoke(pet,"SetSpriteSize",420);
            Check(pet.scene.Portrait.HairStyle==1,"resizing preserves applied choice");
            var mode=(ComboBox)Field(window,"mode");
            var input=(InputFeedbackState)Field(window,"input");
            foreach(int index in new[]{0,1,2,3})
            {
                mode.SelectedIndex=index;Invoke(window,"Tick",null,EventArgs.Empty);
                Check(scene.MusicPlaying==(index==2),"preview modes use isolated simulated music");
                Check(((WardrobePreview)Field(window,"preview")).Resting==(index==3),"rest preview is available");
            }
            Check(input!=pet.inputState,"preview input is isolated from the live pet");
            if(!String.IsNullOrEmpty(previewPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(previewPath));
                mode.SelectedIndex=0;Invoke(window,"Tick",null,EventArgs.Empty);Application.DoEvents();
                using(var image=new Bitmap(window.Width,window.Height))
                {window.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(previewPath,ImageFormat.Png);}
            }
            window.Close();
        }
        using(var reload=new KeyboardPetWindow(root,true,dataRoot:data))
            Check(reload.scene.Portrait.HairStyle==1,"restart restores applied hairstyle");
        string saved=File.ReadAllText(config);
        using(var pet=new KeyboardPetWindow(root,true,dataRoot:data))
        {
            var window=Open(pet);window.SelectHairStyle(0);window.ApplySelection();
            Check(File.ReadAllText(config)==saved&&pet.scene.Portrait.HairStyle==1,"read-only preview cannot apply or save");
        }
        int applies=0;
        using(var window=new PetWardrobeWindow(root,1,hair=>{applies++;throw new IOException("test save failure");}))
        {
            window.SelectHairStyle(0);window.ApplySelection();
            Check(applies==1&&((Label)Field(window,"status")).Text.StartsWith("未保存"),"failed save stays visible to user");
        }
        // A blocked temporary file makes the real save fail before replacement.
        Directory.CreateDirectory(config+".tmp");
        using(var pet=new KeyboardPetWindow(root,false,dataRoot:data))
        {
            var window=Open(pet);window.SelectHairStyle(0);window.ApplySelection();
            Check(pet.scene.Portrait.HairStyle==1&&(int)Field(pet,"hairStyle")==1,"failed save rolls back live and persisted selection");
            Check(File.ReadAllText(config)==saved,"failed save keeps the original configuration");
        }
        Console.WriteLine("PASS: unified wardrobe, isolated previews, apply/cancel, input continuity, save/reload/resize, retired configuration and save failure.");
    }
}
