using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    bool _migrationVerification;int _migrationStage,_migrationTab;double _migrationTimer;long _visualRng;
    int _migrationCapturedTab=-1;System.Threading.Tasks.Task? _migrationCapture;
    bool _migrationInput;int _migrationRoadCoins;Vector2I _migrationRoadCell;
    string MigrationOutput=>OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--test-output="))?[14..]??ProjectSettings.GlobalizePath("res://../.test-output");
    async System.Threading.Tasks.Task MigrationShot(string name)
    {
        if(DisplayServer.GetName()=="headless")return;await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Directory.CreateDirectory(MigrationOutput);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(MigrationOutput,name+".png"));
    }
    void VerifyMigrationScene(double delta)
    {
        try{
            void Check(bool good,string why){if(!good)throw new Exception("MIGRATION_SCENE_FAILED: "+why);}
            _migrationTimer+=delta;if(_frames<8)return;
            if(_migrationStage==0){
                _visualRng=(long)NativeGame.D(Data.Meta,"native_rng");_selectedPet=NativeGame.S(Data.Pets[0]!,"pet_id");
                Game.Execute("buy",new JsonObject{{"item","清水"},{"quantity",3}});Game.Execute("sell",new JsonObject{{"item","清水"},{"quantity",1}});
                foreach(var species in Game.Rules["SPECIES"]!.AsObject())foreach(string pose in new[]{"idle","walk","sleep","eat","bath","pet"}){
                    var mesh=(ArrayMesh)_petModels.Get(species.Key,383,true,false,"成年",pose,1);var array=mesh.SurfaceGetArrays(0);var points=array[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=array[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    Check(points.Length>500&&mesh.GetAabb().Size.Y>0,"原生动物模型缺失");
                    for(int i=0;i<points.Length;i+=3){var cross=(points[i+1]-points[i]).Cross(points[i+2]-points[i]);if(cross.LengthSquared()<1e-10)continue;Check(cross.Dot(normals[i])<.0001,"动物模型三角形朝内");break;}
                }
                _migrationStage=1;_migrationTimer=0;return;
            }
            if(_migrationStage==1){
                if(_migrationTimer<.16)return;
                if(_migrationCapture!=null&&!_migrationCapture.IsCompleted)return;
                if(_migrationTab>0&&_migrationCapturedTab!=_migrationTab&&ManagementTabs[_migrationTab-1] is "外观" or "财务" or "地图" or "档案"){_migrationCapturedTab=_migrationTab;_migrationCapture=MigrationShot("native-migrated-"+_migrationTab);return;}
                if(_migrationTab<ManagementTabs.Length){ShowManagement(ManagementTabs[_migrationTab++]);_migrationTimer=0;return;}
                _management?.Hide();Game.Commons["enabled"]=false;Game.CancelFreight("迁移场景检查");Game.Jobs.Clear();
                double noon=Math.Floor(Game.Now/86400)*86400+12*3600;if(noon<Game.Now)noon+=86400;Game.Meta["sim_ts"]=noon;Game.Meta["start_ts"]=noon-21*86400;Game.Meta["weather"]=Game.Rules["WEATHERS"]!.AsObject().First(p=>p.Key.Contains("雪")&&!p.Key.Contains("雨")).Key;Game.SetZone("ranch",1);Game.Commons["stock"]!["herd"]=6;
                string[] zones={"fields","ranch","park","industry","market","city"};int n=0;
                foreach(var actor in Agents.Where(a=>!a.IsCar).Take(6)){var person=Data.People.First(p=>NativeGame.S(p!,"person_id")==actor.Ident)!;person["destination"]=zones[n++];person["job_id"]="";actor.Path.Clear();actor.Index=0;actor.InsideHome=false;actor.Visual.Show();actor.Position=DistrictWorkPosition(person);actor.ResidentialActivity="现场工作";}
                _center=FarmPet(_selectedPet)!.Position;_distance=12;UpdateCamera();_migrationStage=2;_migrationTimer=0;return;
            }
            if(_migrationStage==2&&_migrationTimer>2){
                Check(_snow!.Emitting&&!_rain!.Emitting&&Game.Season.Contains("冬"),"雨雪或季节切换失败");Check(_herd.Count==6,"公共牲畜未匹配真实数量");
                Check(_performances.Values.Any(p=>p.Kind=="read")&&_performances.Values.Any(p=>p.Kind=="plant")&&_performances.Values.Any(p=>p.Kind=="feed"),"居民工作道具未启用");
                Check((long)NativeGame.D(Data.Meta,"native_rng")==_visualRng,"视觉行为消耗了经营随机数");
                _=MigrationShot("native-migrated-life");Game.Meta["weather"]=Game.Rules["WEATHERS"]!.AsObject().First(p=>p.Key.Contains("雨")).Key;_migrationStage=3;_migrationTimer=0;return;
            }
            if(_migrationStage==3&&_migrationTimer>1){
                Check(_rain!.Emitting&&!_snow!.Emitting,"雨雪粒子不能切换");
                var road=Data.Roads.Keys.First(c=>Data.Roads.ContainsKey(c+Vector2I.Right)&&NativeGame.S(Data.Roads[c],"road_style")!="stone"&&NativeGame.S(Data.Roads[c+Vector2I.Right],"road_style")!="stone"&&Game.RoadReason(new(c.X,c.Y),"stone")==""&&Game.RoadReason(new(c.X+1,c.Y),"stone")=="");
                var from=WorldData.Cell(road);_center=from;_distance=45;UpdateCamera();ArmPlacement("road",new JsonObject{{"style","stone"}});BeginRoadStroke(_camera.UnprojectPosition(from));SampleStroke(road+Vector2I.Right);UpdatePlacement();Check(_strokeCells.Count>=2,"拖拽未连接道路格");
                CancelRoadStroke();_migrationRoadCell=road;_migrationRoadCoins=NativeGame.I(Data.Meta,"coins");_migrationInput=true;
                var start=_camera.UnprojectPosition(from);var end=_camera.UnprojectPosition(WorldData.Cell(road+Vector2I.Right));
                GetViewport().PushInput(new InputEventMouseMotion{Position=start,GlobalPosition=start},true);
                GetViewport().PushInput(new InputEventMouseButton{Position=start,GlobalPosition=start,ButtonIndex=MouseButton.Left,ButtonMask=MouseButtonMask.Left,Pressed=true},true);
                _migrationStage=4;_migrationTimer=0;return;
            }
            if(_migrationStage==4&&_migrationTimer>.2){
                Check(_strokeActive,$"鼠标按下没有开始笔画，界面={GetViewport().GuiGetHoveredControl()?.Name}，提示={_selection}");
                var start=_camera.UnprojectPosition(WorldData.Cell(_migrationRoadCell));var end=_camera.UnprojectPosition(WorldData.Cell(_migrationRoadCell+Vector2I.Right));
                GetViewport().PushInput(new InputEventMouseMotion{Position=end,GlobalPosition=end,Relative=end-start,ButtonMask=MouseButtonMask.Left},true);
                _migrationStage=5;_migrationTimer=0;return;
            }
            if(_migrationStage==5&&_migrationTimer>.2){
                var end=_camera.UnprojectPosition(WorldData.Cell(_migrationRoadCell+Vector2I.Right));
                GetViewport().PushInput(new InputEventMouseButton{Position=end,GlobalPosition=end,ButtonIndex=MouseButton.Left,Pressed=false},true);
                _migrationStage=6;_migrationTimer=0;return;
            }
            if(_migrationStage==6&&_migrationTimer>.2){
                Check(NativeGame.S(Data.Roads[_migrationRoadCell],"road_style")=="stone"&&NativeGame.S(Data.Roads[_migrationRoadCell+Vector2I.Right],"road_style")=="stone","实际鼠标笔画没有提交："+_selection);Check(NativeGame.I(Data.Meta,"coins")==_migrationRoadCoins-28,"实际鼠标笔画费用错误");
                Act("undo_road",new JsonObject());Check(NativeGame.I(Data.Meta,"coins")==_migrationRoadCoins,"实际撤销费用错误");
                var start=_camera.UnprojectPosition(WorldData.Cell(_migrationRoadCell));
                BeginRoadStroke(start);
                Check(_strokeActive&&_strokeCells.Count==1,"测试笔画没有开始");
                SampleStroke(new(-1,-1));
                Check(_strokeCells.Count==1,"无效落点被夹到了地图角落");
                string roadsBefore=Data.Snapshot["roads"]!.ToJsonString();
                _Input(new InputEventMouseButton{Position=new(-8,-8),ButtonIndex=MouseButton.Left,Pressed=false});
                Check(!_strokeActive&&Data.Snapshot["roads"]!.ToJsonString()==roadsBefore&&NativeGame.I(Data.Meta,"coins")==_migrationRoadCoins,"地图外松开误提交道路");
                VerifyDecorationPlacement(Check);
                EndPlacement();
                VerifyTownInterface(Check);
                VerifyJournalFilters(Check);
                VerifyOrderSupplyFlow(Check);
                VerifyFirstDayGuide(Check);
                VerifyGrowthProgress(Check);
                VerifyConstructionProgress(Check);
                VerifyPetClinicFlow(Check);
                VerifyBathSupplyFlow(Check);
                VerifyScenePetCare(Check);
                VerifyTownCommission(Check);
                VerifyOptimizationPresentation(Check);
                VerifyOptimizationWarehouse(Check);
                VerifyOptimizationLists(Check);
                VerifyTownFlowOptimizations(Check);
                VerifyTownPresentationOptimizations(Check);
                VerifyMapEconomyOptimizations(Check);
                VerifyOrderedTownCore(Check);
                VerifyResidentDirectory(Check);
                VerifyManagementReturn(Check);
                VerifyTownLifeScene(Check);
                VerifyTownCloseFailure(Check);
                VerifyTownContinuity(Check);
                VerifyTownUiExpansion(Check);
                VerifyTownHudRefresh(Check);
                VerifyManagementShell(Check);
                VerifyTownMapTools(Check);
                VerifyOperationalInterface(Check);
                VerifyTownSearchExperience(Check);
                VerifyResidentExperience(Check);
                VerifyJournalExperience(Check);
                VerifyTownSettingsExperience(Check);
                VerifyWorkshopExperience(Check);
                VerifyBlueprintExperience(Check);
                VerifyFinanceExperience(Check);
                VerifyCameraExperience(Check);
                VerifyCareExperience(Check);
                VerifyFarmExperience(Check);
                VerifySaveExperience(Check);
                VerifyMapSurvey(Check);
                VerifyFreightInterface(Check);
                VerifyHousingExperience(Check);
                VerifyGrowthExperience(Check);
                VerifyAppearanceWorkshop(Check);
                VerifyTourismExperience(Check);
                VerifyTownProjectExperience(Check);
                VerifyArchiveExperience(Check);
                VerifyPhotoAlbumWorkshop(Check);
                VerifyFestivalExperience(Check);
                VerifyDecorationExperience(Check);
                VerifyTimePlanner(Check);
                _migrationInput=false;EndPlacement();
                GD.Print($"MIGRATION_SCENE_VERIFIED tabs={ManagementTabs.Length} models=72 weather_seasons_herd_work_props_mouse_road_undo_rng_off_map_decoration_live_journal_filters_station_follow_order_supply_first_day_growth_construction_map_reconnect_clinic_supply_scene_care_camera_district_ambience_pet_motion_display_warehouse_order_filters_roster_seasonal_commission_shadow_hover_nameplate_map_clusters_photo_fast_forward_batch_preview_autonomy_snapshot_history");_migrationStage=7;GetTree().Quit();
            }
        }catch(Exception ex){GD.PushError(ex.ToString());_migrationStage=7;GetTree().Quit(1);}
    }

    void VerifyJournalFilters(Action<bool,string> check)
    {
        var orders=Game.Rows("orders");
        var added=Enumerable.Range(0,JournalPageSize*2+2).Select(index=>new JsonObject{
            {"order_id",$"journal_page_test_{index}"},{"npc","邻居"},{"item","清水"},{"kind","item"},{"quantity",1},
            {"created_ts",Game.Now},{"expiry_ts",Game.Now+(index+1)*60},{"status","进行中"},{"reward",15}
        }).ToArray();
        foreach(var order in added)orders.Add(order);
        ShowManagement("今日待办");
        var body=_managementBody!;
        Button ButtonOnPage(string name)=>(Button)body.FindChild(name,true,false)!;
        var filter=ButtonOnPage("JournalFilter_订单");
        filter.EmitSignal(BaseButton.SignalName.Pressed);
        check(filter.ButtonPressed&&body.FindChild("JournalCard_订单_journal_page_test_0",true,false)!=null,"订单筛选没有显示首页订单");
        check(body.FindChildren("JournalCard_宠物_*","PanelContainer",true,false).Count==0,"筛选订单后仍混入伙伴照护");
        var next=ButtonOnPage("JournalNextPage");
        check(!next.Disabled,"待办过多时无法翻页");
        next.EmitSignal(BaseButton.SignalName.Pressed);
        next.EmitSignal(BaseButton.SignalName.Pressed);
        string lastId=NativeGame.S(added[^1],"order_id");
        var last=body.FindChild($"JournalCard_订单_{lastId}",true,false);
        check(last!=null&&next.Disabled,"排在待办列表第 16 项后的订单没有显示");
        ((Button)last!.FindChild("JournalOpen",true,false)!).EmitSignal(BaseButton.SignalName.Pressed);
        check(_tab=="订单"&&_managementEntryTarget==lastId,"分页待办没有跳转到正确订单");
        ShowManagement("今日待办");
        check(ButtonOnPage("JournalFilter_订单").ButtonPressed&&body.FindChild($"JournalCard_订单_{lastId}",true,false)!=null,"返回待办后丢失筛选或页码");
        foreach(var order in added)orders.Remove(order);
        RefreshManagementPage(1);
        check(_journalPage==0,"待办更新后没有收回空页");
        ButtonOnPage("JournalFilterAll").EmitSignal(BaseButton.SignalName.Pressed);
        _management?.Hide();
    }

    void VerifyPetClinicFlow(Action<bool,string> check)
    {
        var pets=Data.Pets.OfType<JsonNode>().Where(Game.Active).Take(2).ToArray();
        var pet=pets[0];
        var occupant=pets[1];
        string id=NativeGame.S(pet,"pet_id");
        string occupantId=NativeGame.S(occupant,"pet_id");
        int home=NativeGame.I(pet,"building_id"),occupantHome=NativeGame.I(occupant,"building_id");
        int hospital=NativeGame.I(Game.Rows("buildings").OfType<JsonNode>().First(plot=>
            NativeGame.B(plot,"unlocked")&&NativeGame.S(plot,"kind")==""),"building_id");
        _management?.Hide();
        Act("build_manor",new JsonObject{{"building_id",hospital},{"kind","宠物医院"}});
        occupant["pregnancy_due_ts"]=Game.Now+NativeGame.Day;
        Game.Execute("move",new JsonObject{{"pet_id",occupantId},{"building_id",hospital}});
        SyncBusinessWorld();
        pet["illness_type"]="普通感染";
        pet["status"]="生病";
        pet["injury"]=0;
        pet["treatment_ready_ts"]=0;
        ShowManagement("今日待办");
        var body=_managementBody!;
        var journal=body.FindChild($"JournalCard_宠物_{id}",true,false);
        check(journal!=null,"需要治疗的伙伴没有进入待办");
        ((Button)journal!.FindChild("JournalOpen",true,false)!).EmitSignal(BaseButton.SignalName.Pressed);
        check(_tab=="宠物"&&_selectedPet==id&&_managementFocusTarget?.Name=="PetClinic","待办没有打开对应伙伴的诊疗区");

        Button ButtonOnPage(string name)=>(Button)body.FindChild(name,true,false)!;
        void Press(string name)
        {
            var button=ButtonOnPage(name);
            check(button is { Disabled:false }&&button.IsVisibleInTree(),$"诊疗操作不可用：{name}");
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        var admit=ButtonOnPage("PetClinicAction");
        check(admit.Disabled&&Game.PetHospitalAdmission(pet).FreeBeds==0,"满床时仍能点击送医或没有为孕期伙伴预留床位");
        occupant["pregnancy_due_ts"]=0;
        RefreshManagementPage(1);
        check(admit.IsInsideTree()&&!admit.Disabled,"腾出床位后送医按钮没有原地更新");
        Press("PetClinicAction");
        check(Game.IsPetInHospital(pet),"送医操作没有让伙伴入院");

        int medicine=Game.Stock("药品");
        if(medicine>0)Game.Take("药品",medicine);
        RefreshManagementPage(1);
        check(ButtonOnPage("PetClinicAction").Disabled,"缺药时仍能点击治疗");
        Press("PetCareSupply_treat");
        check(_tab=="商店","缺药时补货入口没有打开商店");
        pet["illness_type"]="";
        pet["status"]="正常";
        RefreshManagementPage(1);
        check(ButtonOnPage("CareSupplyMedicineBuy").Disabled,"伙伴不再需要治疗后仍能从原入口购买药品");
        pet["illness_type"]="普通感染";
        pet["status"]="生病";
        RefreshManagementPage(1);
        Press("CareSupplyMedicineBuy");
        check(Game.Stock("药品")==1&&Game.NeedsPetTreatment(pet),"采购药品应只补充库存");
        Press("CareSupplyReturn");
        check(_tab=="宠物"&&_selectedPet==id&&_managementFocusTarget?.Name=="PetClinic","补货后没有返回原伙伴诊疗区");
        Press("PetClinicAction");
        check(!Game.NeedsPetTreatment(pet)&&Game.IsPetInHospital(pet)&&Game.Stock("药品")==0,"治疗没有按规则消耗药品并更新病情");
        UpdateJournalBadge();
        check(_journalEntries.Any(entry=>entry.Id==id&&entry.Title.Contains("出院")),"康复后没有显示出院待办");
        Press("PetClinicDischarge");
        check(!Game.IsPetInHospital(pet)&&NativeGame.I(pet,"building_id")==home,"出院没有迁回适配居所");
        UpdateJournalBadge();
        check(!_journalEntries.Any(entry=>entry.Id==id&&entry.Title.Contains("出院")),"完成出院后待办仍存在");
        _management?.Hide();
        Game.Execute("move",new JsonObject{{"pet_id",occupantId},{"building_id",occupantHome}});
        SyncBusinessWorld();
    }

    void VerifyBathSupplyFlow(Action<bool,string> check)
    {
        var pet=Data.Pets.OfType<JsonNode>().First(Game.Active);
        string id=NativeGame.S(pet,"pet_id");
        pet["cleanliness"]=30;
        foreach(string item in new[]{"清水","沐浴露"}){
            int stock=Game.Stock(item);
            if(stock>0)Game.Take(item,stock);
        }
        void Press(string name)
        {
            var button=_managementBody!.FindChild(name,true,false) as Button;
            check(button is { Disabled:false }&&button.IsVisibleInTree(),$"洗澡补货操作不可用：{name}");
            button!.EmitSignal(BaseButton.SignalName.Pressed);
        }
        OpenPetManagement(id,"care:bath");
        Press("PetCareSupply_bath");
        Press("CareSupplyWaterBuy");
        check(Game.Stock("清水")==1&&Game.Stock("沐浴露")==0,"洗澡补货没有单独购买清水");
        Press("CareSupplySoapBuy");
        check(Game.Stock("沐浴露")==1&&NativeGame.D(pet,"cleanliness")==30,"补货没有保留独立的洗澡操作");
        Press("CareSupplyReturn");
        check(_tab=="宠物"&&_selectedPet==id&&_managementEntryTarget=="care:bath"&&_managementFocusTarget!=null,"补货后没有回到原伙伴的洗澡操作");
        _management?.Hide();
    }

    void VerifyScenePetCare(Action<bool,string> check)
    {
        var pet=Data.Pets.OfType<JsonNode>().Where(Game.Active).Last();
        string id=NativeGame.S(pet,"pet_id");
        var feed=(Button)_panel.FindChild("ScenePetFeed",true,false);
        var details=(Button)_panel.FindChild("ScenePetCareDetails",true,false);
        var hunger=(Label)_panel.FindChild("ScenePetHunger",true,false);
        int stock=Game.Stock("普通饲料");
        _selectedPet="";
        RefreshScenePetCare();
        check(feed.Disabled,"没有选择伙伴时场景仍能喂食");
        feed.EmitSignal(BaseButton.SignalName.Pressed);
        check(Game.Stock("普通饲料")==stock,"未选择伙伴时消耗了饲料");
        check(LocatePet(id),"无法定位场景照护验证用伙伴");
        pet["hunger"]=31;
        RefreshScenePetCare();
        check(hunger.IsVisibleInTree()&&hunger.Text.Contains("31"),"场景照护没有原地更新饱食数值");
        var play=(Button)_panel.FindChild("ScenePetPlay",true,false);
        var reasons=(Label)_panel.FindChild("ScenePetCareReasons",true,false);
        double originalReady=NativeGame.D(pet,"interact_ready_ts");
        pet["interact_ready_ts"]=Game.Now+NativeGame.Hour;
        RefreshScenePetCare();
        check(play.Disabled&&reasons.Text.Contains("互动冷却中"),"场景照护没有明确说明互动冷却");
        pet["interact_ready_ts"]=Game.Now;
        RefreshScenePetCare();
        play.EmitSignal(BaseButton.SignalName.Pressed);
        check(play.Disabled&&NativeGame.D(pet,"interact_ready_ts")>Game.Now,"场景互动没有应用实际照护规则");
        pet["interact_ready_ts"]=originalReady;
        check(!details.Disabled,"选中实际场景伙伴后无法打开照护详情");
        details.EmitSignal(BaseButton.SignalName.Pressed);
        check(_tab=="宠物"&&_selectedPet==id,"场景照护详情没有打开所选伙伴");
        _management?.Hide();
    }

    void VerifyOrderSupplyFlow(Action<bool,string> check)
    {
        void AddOrder(string id,string item,int needed) => Game.Rows("orders").Add(new JsonObject{
            {"order_id",id},{"npc","小镇邻居"},{"item",item},{"kind","item"},{"quantity",needed},
            {"created_ts",Game.Now},{"expiry_ts",Game.Now+2*NativeGame.Hour},{"status","进行中"},
            {"reward",40},{"reputation",1}
        });
        void Press(Node owner,string name)
        {
            var button=owner.FindChild(name,true,false) as Button;
            check(button is { Visible:true,Disabled:false },$"缺少可操作按钮 {name}");
            button!.EmitSignal(BaseButton.SignalName.Pressed);
        }

        const string waterOrder="verify_supply_water";
        int initialStock=Game.Stock("清水"),initialCoins=NativeGame.I(Data.Meta,"coins");
        AddOrder(waterOrder,"清水",initialStock+2);
        ShowManagement("订单",waterOrder);
        var body=_managementBody!;
        var card=body.FindChild($"OrderCard_{waterOrder}",true,false);
        check(card!=null,"订单页面缺少指定的缺货卡片");
        Press(card!,"OrderBuySupply");
        check(_tab=="商店"&&Game.Stock("清水")==initialStock&&NativeGame.I(Data.Meta,"coins")==initialCoins,"跳转补货时不应自动购买");
        check(body.FindChild("OrderSupplyQuantity",true,false) is SpinBox { Value:2 },"采购数量没有预填实际缺口");
        var supplyQuantity=body.FindChild("OrderSupplyQuantity",true,false);
        Press(body,"OrderSupplyBuy");
        check(Game.Stock("清水")==initialStock+2,"从订单补货没有按缺口买入");
        check(ReferenceEquals(supplyQuantity,body.FindChild("OrderSupplyQuantity",true,false)),"订单补货后整页被重建");
        Press(body,"OrderSupplyReturn");
        check(_tab=="订单"&&_managementEntryTarget==waterOrder&&_managementFocusTarget!=null,"补货后没有回到原订单卡片");

        const string recipeOrder="verify_supply_recipe";
        AddOrder(recipeOrder,"高级饲料",Game.Stock("高级饲料")+1);
        ShowManagement("订单",recipeOrder);
        Press(body.FindChild($"OrderCard_{recipeOrder}",true,false)!,"OrderCraftSupply");
        check(_tab=="商店"&&body.FindChild("OrderSupplyCraft",true,false) is Button,"订单未能直达对应加工配方");
        var recipe=Game.Rows("orders").OfType<JsonNode>().Single(row=>NativeGame.S(row,"order_id")==recipeOrder);
        recipe["expiry_ts"]=Game.Now;
        Press(body,"OrderSupplyReturn");
        check(_tab=="订单"&&_managementEntryTarget==recipeOrder&&_managementFocusTarget!=null,"订单到期后无法返回查看原订单状态");
        _management?.Hide();
    }

    void VerifyFirstDayGuide(Action<bool,string> check)
    {
        string petId=NativeGame.S(Data.Pets.OfType<JsonNode>().First(Game.Active),"pet_id");
        StartFirstDayGuide();
        check(_firstDayGuidePanel?.Visible==true&&FirstDayGuideProgress==0,"新手引导没有从第一步开始");
        Act("feed",new JsonObject{{"pet_id",petId},{"item","普通饲料"}});
        check((FirstDayGuideProgress&1)==1,"实际喂食后没有勾选引导步骤");
        check(LocatePet(petId)&&(FirstDayGuideProgress&2)==2,"定位宠物后没有勾选引导步骤");
        ShowManagement("订单");
        check((FirstDayGuideProgress&7)==7,"打开订单后没有完成新手引导");
        var growthLink=_managementBody!.FindChild("FirstDayGrowthLink",true,false) as Button;
        check(growthLink is { Visible:true,Disabled:false },"引导完成后没有提供成长路线入口");
        ShowManagement("今日待办");
        growthLink=_managementBody.FindChild("FirstDayGrowthLink",true,false) as Button;
        check(growthLink is { Visible:true,Disabled:false },"切换管理页后成长路线入口消失");
        growthLink!.EmitSignal(BaseButton.SignalName.Pressed);
        check(_tab=="成长"&&!_firstDayGrowthOffer,"引导没有跳转到成长路线");
        _management?.Hide();
        StartFirstDayGuide();
        var skip=_firstDayGuidePanel!.FindChildren("*","Button",true,false).OfType<Button>()
            .Single(button=>button.Text=="跳过引导");
        skip.EmitSignal(BaseButton.SignalName.Pressed);
        check((FirstDayGuideProgress&8)==8&&!_firstDayGuidePanel.Visible,"跳过后引导没有关闭或保存跳过标记");
    }

    void VerifyGrowthProgress(Action<bool,string> check)
    {
        const string id="verify_supply_water";
        var order=Game.Rows("orders").OfType<JsonNode>().Single(row=>NativeGame.S(row,"order_id")==id);
        Game.Meta["stats"]!["orders"]=2;
        ShowManagement("成长");
        var progress=_managementBody!.FindChild("GrowthProgress_orders",true,false) as ProgressBar;
        check(progress is { Value:2,MaxValue:3 },"成长路线没有显示真实的 2/3 订单进度");
        int coins=NativeGame.I(Data.Meta,"coins");
        int reward=Game.Rules["ACHIEVEMENTS"]!["orders"]![3]!.GetValue<int>();
        Game.Execute("order",new JsonObject{{"order_id",id}});
        RefreshManagementPage(1);
        check(progress!.IsInsideTree()&&progress.Value==3,"交付第三单后进度没有原地更新");
        check(Game.State["achievements"]!["orders"]!=null&&NativeGame.I(Data.Meta,"coins")==coins+reward+NativeGame.I(order,"reward"),"成就奖励与原有规则不符");
        int earnedCoins=NativeGame.I(Data.Meta,"coins");
        var pet=Data.Pets.OfType<JsonNode>().First(Game.Active);
        Game.Execute("rename",new JsonObject{{"pet_id",NativeGame.S(pet,"pet_id")},{"name",NativeGame.S(pet,"name")}});
        check(NativeGame.I(Data.Meta,"coins")==earnedCoins,"查看或刷新成长进度后重复发放成就奖励");
        _management?.Hide();
    }

    void VerifyTownInterface(Action<bool,string> check)
    {
        var pet=Data.Pets.OfType<JsonNode>().First(Game.Active);
        string id=NativeGame.S(pet,"pet_id");
        OpenPetManagement(id);
        var body=_managementBody!;
        var hunger=body.FindChildren("*","ProgressBar",true,false).OfType<ProgressBar>().First();
        var play=body.FindChildren("*","Button",true,false).OfType<Button>().Single(button=>button.Text=="互动");
        pet["hunger"]=30;
        pet["interact_ready_ts"]=Game.Now+60;
        RefreshManagementPage(1);
        check(hunger.Value==30&&play.Disabled&&hunger.IsInsideTree(),"照护状态没有原地刷新");
        pet["interact_ready_ts"]=Game.Now;
        RefreshManagementPage(1);
        check(!play.Disabled&&hunger.IsInsideTree(),"互动冷却结束后没有恢复按钮");

        ShowManagement("今日待办");
        check(_journalEntries.Any(entry=>entry.Id==id&&entry.Page=="宠物"),"需要照护的伙伴没有进入待办");
        ShowManagement("订单");
        var order=Game.Rows("orders").OfType<JsonNode>().First(entry=>NativeGame.S(entry,"status")=="进行中");
        order["expiry_ts"]=Game.Now+60;
        RefreshManagementPage(1);
        check(body.FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains("剩余 1 游戏分钟")),"订单没有原地更新倒计时");

        OpenPetActivityStation("bowl");
        check(_tab=="宠物"&&_managementEntryTarget=="care:feed"&&_managementFocusTarget!=null,"食盆没有定位到喂食操作");
        check(StartPetFollow(id),"无法进入宠物跟随");
        var actor=FarmPet(id)!;
        actor.Position+=Vector3.Right*3;
        UpdatePetFollow(.5);
        check(_center.DistanceTo(actor.Position)<.1f,"宠物移动后跟随镜头没有移动");
        _UnhandledInput(new InputEventKey{Keycode=Key.Escape,Pressed=true});
        check(_followPet==null,"Esc 没有退出跟随");
    }

    void VerifyConstructionProgress(Action<bool,string> check)
    {
        const string catalog="bus_stop";
        var cell=Enumerable.Range(0,125*80).Select(i=>new Vector2I(i%125,i/125))
            .First(c=>Game.PlacementReason(catalog,c.X,c.Y,0)=="");
        Act("build_town",new JsonObject{{"catalog_key",catalog},{"cx",cell.X},{"cy",cell.Y},{"orientation",0}});
        var project=Game.Rows("town_buildings").OfType<JsonNode>().Last();
        string id=NativeGame.S(project,"building_id");
        check(NativeGame.S(project,"catalog_key")==catalog&&NativeGame.S(project,"status")=="building","未能开始施工回归项目");
        var initialMesh=((MeshInstance3D)_dynamic[$"town_building:{id}"]).Mesh;
        check(initialMesh==Prototype($"town:{catalog}:0"),"施工开始时场景未显示第一阶段");
        _buildWorkspace=2;
        ShowManagement("建造",id);
        var body=_managementBody!;
        var progress=body.FindChild("ConstructionProgress",true,false) as ProgressBar;
        check(progress is { Value:0 }&&body.FindChild("ConstructionRoadStatus",true,false) is Label,"施工页没有显示进度和接路状态");

        double remaining=NativeGame.D(project,"finish_ts")-Game.Now;
        Game.Meta["native_next_hour"]=Game.Now+NativeGame.Hour;
        Game.Advance(remaining/2);
        SyncBusinessWorld();
        RefreshManagementPage(1);
        var halfwayMesh=((MeshInstance3D)_dynamic[$"town_building:{id}"]).Mesh;
        check(NativeGame.S(project,"status")=="building"&&halfwayMesh==Prototype($"town:{catalog}:2"),"半程施工没有更新模型阶段");
        check(progress!.IsInsideTree()&&Math.Abs(progress.Value-50)<.1,"施工页的进度没有原地更新");
        VerifyMapNavigation(project,check);
        var locate=body.FindChild("LocateConstruction",true,false) as Button;
        check(locate is { Visible:true,Disabled:false },"缺少施工现场定位按钮");
        locate!.EmitSignal(BaseButton.SignalName.Pressed);
        Vector3 expected=new((cell.X+NativeGame.I(project,"w")*.5f)*WorldData.GridSize,0,(cell.Y+NativeGame.I(project,"h")*.5f)*WorldData.GridSize);
        check(_management?.Visible==false&&_center.DistanceTo(expected)<.1f,"定位施工项目没有移到正确地块");

        Game.Advance(remaining/2);
        SyncBusinessWorld();
        UpdateJournalBadge();
        check(NativeGame.S(project,"status")=="ready"&&((MeshInstance3D)_dynamic[$"town_building:{id}"]).Mesh==Prototype($"town:{catalog}:4"),"完工后没有切换到最终模型");
        check(_journalEntries.Count(entry=>entry.Page=="建造"&&entry.Id==id)==1,"完工待办丢失或重复");
        ShowManagement("今日待办");
        var dismiss=body.FindChild("DismissConstructionNotice",true,false) as Button;
        check(dismiss is { Visible:true,Disabled:false },"完工待办没有已读操作");
        dismiss!.EmitSignal(BaseButton.SignalName.Pressed);
        UpdateJournalBadge();
        check(!_journalEntries.Any(entry=>entry.Page=="建造"&&entry.Id==id),"确认已读后完工待办仍存在");
    }

    void VerifyMapNavigation(JsonNode project,Action<bool,string> check)
    {
        var isolated=Enumerable.Range(0,125*80).Select(i=>new Vector2I(i%125,i/125)).First(c=>
            !Data.Roads.ContainsKey(c)&&!Data.Roads.ContainsKey(c+Vector2I.Up)&&!Data.Roads.ContainsKey(c+Vector2I.Down)
            &&!Data.Roads.ContainsKey(c+Vector2I.Left)&&!Data.Roads.ContainsKey(c+Vector2I.Right)&&Game.RoadReason(new(c.X,c.Y),"stone")=="");
        Act("road",new JsonObject{{"cx",isolated.X},{"cy",isolated.Y},{"style","stone"}});
        check(Data.Roads.ContainsKey(isolated)&&!Game.ConnectedRoads.Contains(new(isolated.X,isolated.Y)),"没有创建地图验证用的断开道路");

        TownMap OpenMap()
        {
            ShowManagement("地图");
            var control=_managementBody!.FindChild("TownNavigationMap",true,false) as TownMap;
            check(control!=null,"地图页缺少导航控件");
            control!.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            control.Size=new(620,400);
            control.ResetView();
            return control;
        }
        void Click(TownMap map,Vector2 marker)
        {
            map._GuiInput(new InputEventMouseButton{Position=marker,ButtonIndex=MouseButton.Left,Pressed=true});
            map._GuiInput(new InputEventMouseButton{Position=marker,ButtonIndex=MouseButton.Left,Pressed=false});
        }

        var map=OpenMap();
        Vector2 anchor=new(340,182);
        Vector2 worldAtCursor=map.ViewToWorld(anchor);
        map._GuiInput(new InputEventMouseButton{Position=anchor,ButtonIndex=MouseButton.WheelUp,Pressed=true});
        check(worldAtCursor.DistanceTo(map.ViewToWorld(anchor))<.5f,"地图滚轮放大时光标下的坐标发生偏移");

        var cameraBeforeDrag=_center;
        Vector2 end=anchor+new Vector2(38,26);
        map._GuiInput(new InputEventMouseButton{Position=anchor,ButtonIndex=MouseButton.Left,Pressed=true});
        map._GuiInput(new InputEventMouseMotion{Position=end,Relative=end-anchor,ButtonMask=MouseButtonMask.Left});
        map._GuiInput(new InputEventMouseButton{Position=end,ButtonIndex=MouseButton.Left,Pressed=false});
        check(_center==cameraBeforeDrag&&worldAtCursor.DistanceTo(map.ViewToWorld(anchor))>1,"地图拖拽没有平移或错误触发了场景定位");

        var edge=end-Vector2.Right*950;
        map._GuiInput(new InputEventMouseButton{Position=end,ButtonIndex=MouseButton.Middle,Pressed=true});
        map._GuiInput(new InputEventMouseMotion{Position=edge,ButtonMask=MouseButtonMask.Middle});
        var atEdge=map.ViewToWorld(anchor);
        map._GuiInput(new InputEventMouseMotion{Position=edge+Vector2.Right*12,ButtonMask=MouseButtonMask.Middle});
        map._GuiInput(new InputEventMouseButton{Position=edge+Vector2.Right*12,ButtonIndex=MouseButton.Middle});
        check(map.ViewToWorld(anchor).X<atEdge.X-1&&_center==cameraBeforeDrag,"地图推到边缘再反向拖动时存在死区或误定位");

        map.ResetView();
        var brokenRoad=WorldData.Cell(isolated);
        Vector2 roadPoint=new(brokenRoad.X,brokenRoad.Z);
        int balance=NativeGame.I(Data.Meta,"coins");
        var repair=Game.PreviewReconnect(new(isolated.X,isolated.Y),"stone");
        check(repair is { Cells.Length:>0,Reason.Length:0 },"测试路段没有可行的接线方案");
        Click(map,map.WorldToView(roadPoint));
        var confirm=_managementBody!.FindChild("ConfirmRoadReconnect",true,false) as Button;
        check(confirm is { Disabled:false }&&confirm.IsVisibleInTree()&&confirm.Text.Contains(repair.Cost.ToString()),"点击断路没有显示接线报价");
        check(NativeGame.I(Data.Meta,"coins")==balance&&!Game.ConnectedRoads.Contains(new(isolated.X,isolated.Y)),"接线预览修改了路网或金币");
        ((Button)_managementBody.FindChild("LocateDisconnectedRoad",true,false)!).EmitSignal(BaseButton.SignalName.Pressed);
        check(_management?.Visible==false&&_center.DistanceTo(brokenRoad)<.5f,"地图断开道路标记没有定位到正确路段");
        map=OpenMap();
        Click(map,map.WorldToView(roadPoint));
        ((Button)_managementBody.FindChild("ConfirmRoadReconnect",true,false)!).EmitSignal(BaseButton.SignalName.Pressed);
        check(Game.ConnectedRoads.Contains(new(isolated.X,isolated.Y))&&NativeGame.I(Data.Meta,"coins")==balance-repair.Cost,"地图确认没有按报价接通道路");

        map=OpenMap();
        var site=TownProjectCenter(project);
        var marker=map.WorldToView(new(site.X,site.Z));
        map._GuiInput(new InputEventMouseMotion{Position=marker});
        var details=_managementBody!.FindChild("TownMapDetails",true,false) as Label;
        check(details!=null&&details.Text.Contains("施工进度"),"悬停施工标记时没有显示项目进度");
        var toggle=(CheckButton)_managementBody.FindChild("TownMapConstruction",true,false);
        toggle.ButtonPressed=false;
        check(!map.ShowConstruction&&!details!.Text.Contains("施工进度"),"关闭施工图层后仍显示施工标记信息");
        map=OpenMap();
        toggle=(CheckButton)_managementBody.FindChild("TownMapConstruction",true,false);
        check(!toggle.ButtonPressed&&!map.ShowConstruction,"重开地图后丢失了图层选择");
        toggle.ButtonPressed=true;
        Click(map,map.WorldToView(new(site.X,site.Z)));
        check(_tab=="建造"&&_managementEntryTarget==NativeGame.S(project,"building_id"),"点击地图施工标记没有打开对应工程");
    }

    void VerifyTownCommission(Action<bool,string> check)
    {
        const string key="native_town_commission";
        double now=Game.Now;
        var savedCommission=Data.Meta[key]?.DeepClone();
        var savedFreight=Game.FreightState.DeepClone();
        Data.Meta.Remove(key);
        var preparation=Game.TownCommission();
        foreach(var item in preparation.Plan.Supplies){
            int missing=item.Count-Game.Stock(item.Item);
            if(missing>0)Game.Add(item.Item,missing);
        }
        check(Game.TownCommission() is { Stage:0,SuppliesReady:true,RoadReady:true,CanPrepare:true },"季节委托没有显示可备货状态");
        ShowManagement("成长");
        Button CommissionButton()=>(Button)_managementBody!.FindChild("TownCommissionAction",true,false)!;
        var prepare=CommissionButton();
        check(!prepare.Disabled&&prepare.Text=="确认备货","成长页不能开始季节委托");
        var stocks=preparation.Plan.Supplies.ToDictionary(item=>item.Item,item=>Game.Stock(item.Item));
        prepare.EmitSignal(BaseButton.SignalName.Pressed);
        check(Game.TownCommission().Stage==1&&stocks.All(item=>Game.Stock(item.Key)==item.Value),"备货应保留实际库存");

        var driver=Game.People.OfType<JsonNode>().First(person=>NativeGame.S(person,"role")=="居民"&&Game.ResidentAgeDays(person)>=7);
        Game.FreightState["driver_id"]=NativeGame.S(driver,"person_id");
        Game.FreightState["phase"]="unloading";
        Game.FreightState["cargo_item"]="food";
        Game.FreightState["cargo_count"]=1;
        int funds=NativeGame.I(Game.Commons,"funds");
        check(Game.DeliverFreight(),"验证用居民货运未能卸货");
        check(Game.TownCommission() is { Stage:1,FreightArrived:true },"完成货运后应等待次日交付");
        Game.Meta["sim_ts"]=Game.TownCommission().Opens;
        RefreshManagementPage(1);
        var deliver=CommissionButton();
        check(!deliver.Disabled&&deliver.IsVisibleInTree()&&deliver.Text=="交付本季委托","次日开放后没有显示交付操作");
        int coins=NativeGame.I(Data.Meta,"coins"),reputation=NativeGame.I(Data.Meta,"reputation");
        int season=Game.TownCommissionSeason;
        deliver.EmitSignal(BaseButton.SignalName.Pressed);
        check(Game.TownCommission().Stage==3&&preparation.Plan.Supplies.All(item=>Game.Stock(item.Item)==stocks[item.Item]-item.Count)
            &&NativeGame.I(Data.Meta,"coins")==coins+preparation.Plan.Coins
            &&NativeGame.I(Data.Meta,"reputation")==reputation+preparation.Plan.Reputation,"交付没有准确消耗库存和发放奖励");
        bool rejected=false;
        try{Game.Execute("town_commission",new JsonObject{{"step","deliver"},{"season",season}});}
        catch(InvalidOperationException){rejected=true;}
        check(rejected&&NativeGame.I(Data.Meta,"coins")==coins+preparation.Plan.Coins,"已完成委托可以重复领取");
        Game.Meta["sim_ts"]=preparation.Deadline;
        check(Game.TownCommission().Stage==0&&Game.TownCommission().Plan.Name!=preparation.Plan.Name,"换季后没有轮换委托");
        Game.Meta["sim_ts"]=now;
        Game.Meta[key]=savedCommission;
        Game.Meta["native_freight"]=savedFreight;
        Game.Commons["funds"]=funds;
        Data.Reindex();
        _management?.Hide();
    }

    void VerifyDecorationPlacement(Action<bool,string> check)
    {
        const string kind="bench";
        var cells=Enumerable.Range(0,80).SelectMany(y=>Enumerable.Range(0,125).Select(x=>new Vector2I(x,y)))
            .OrderBy(cell=>Math.Abs(cell.X-_migrationRoadCell.X)+Math.Abs(cell.Y-_migrationRoadCell.Y))
            .Where(cell=>Game.DecorationQuote(kind,cell.X,cell.Y,0).Reason=="").Take(2).ToArray();
        check(cells.Length==2,"找不到装饰测试地块");
        void SelectAt(Vector2I cell)
        {
            _center=WorldData.Cell(cell);
            UpdateCamera();
            Select(_camera.UnprojectPosition(_center));
        }
        int count=Data.Meta["decorations"]!.AsArray().Count;
        ArmPlacement("decoration",new JsonObject{{"operation","place"},{"kind",kind},{"orientation",0}});
        UpdateDecorationPlacement(_migrationRoadCell);
        check(_decorationFootprint!.Visible&&_decorationFootprint.MaterialOverride==_ghostBad,"道路上的装饰预览没有标红");
        SelectAt(_migrationRoadCell);
        check(Data.Meta["decorations"]!.AsArray().Count==count,"道路上的装饰不应摆放");
        _UnhandledInput(new InputEventKey{Keycode=Key.R,Pressed=true});
        UpdateDecorationPlacement(cells[0]);
        check(_ghost!.Visible&&_ghost.MaterialOverride==_ghostGood,"可摆放的装饰没有显示预览");
        SelectAt(cells[0]);
        var decoration=Data.Meta["decorations"]!.AsArray().Last()!;
        check(NativeGame.I(decoration,"cx")==cells[0].X&&NativeGame.I(decoration,"cy")==cells[0].Y&&NativeGame.I(decoration,"orientation")==1,"装饰未按预览落位并旋转");
        string id=NativeGame.S(decoration,"id");
        int coins=NativeGame.I(Data.Meta,"coins");
        check(coins==_migrationRoadCoins-Game.DecorationQuote(kind,cells[1].X,cells[1].Y,0).Cost,"场景摆放费用与报价不一致");

        ArmPlacement("decoration",new JsonObject{{"operation","move"},{"id",id},{"kind",kind},{"orientation",1}});
        _UnhandledInput(new InputEventKey{Keycode=Key.R,Pressed=true});
        SelectAt(cells[1]);
        decoration=Data.Meta["decorations"]!.AsArray().Single(row=>NativeGame.S(row!,"id")==id)!;
        check(_placementAction==""&&NativeGame.I(decoration,"cx")==cells[1].X&&NativeGame.I(decoration,"cy")==cells[1].Y&&NativeGame.I(decoration,"orientation")==2,"场景移动未完成或没有结束预览");
        check(NativeGame.I(Data.Meta,"coins")==coins,"移动装饰不应收费");
        Act("decoration",new JsonObject{{"operation","undo"}});
        decoration=Data.Meta["decorations"]!.AsArray().Single(row=>NativeGame.S(row!,"id")==id)!;
        check(NativeGame.I(decoration,"cx")==cells[0].X&&NativeGame.I(decoration,"cy")==cells[0].Y,"撤销没有恢复装饰位置");
        Act("decoration",new JsonObject{{"operation","undo"}});
        check(Data.Meta["decorations"]!.AsArray().Count==count&&NativeGame.I(Data.Meta,"coins")==_migrationRoadCoins,"撤销没有恢复装饰数量和费用");
    }
}
