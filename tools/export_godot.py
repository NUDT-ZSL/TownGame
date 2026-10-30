"""一次性导出网格和旧存档快照。Godot运行时不调用Python。"""
import argparse
import base64
import json
import os
from pathlib import Path
import sqlite3
import sys
import tempfile
from contextlib import closing
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
os.environ['PYGAME_HIDE_SUPPORT_PROMPT']='1'
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
import pygame
from db import Database
from game import Game
from interface import ManorApp
from world3d import World3D,person_mesh,vehicle_mesh,pet_mesh,house_mesh,decoration_mesh
from mesh3d import road_mesh
from town_grid import PORTALS,terrain
import town_catalog as K
import game_config as C
import decorations


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--db',type=Path)
    args=parser.parse_args()
    out=ROOT/'native-godot'/'Content';out.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(dir=ROOT/'.test-output') as folder:
        path=Path(folder)/'copy.db'
        if args.db:
            source=sqlite3.connect(args.db.resolve().as_uri()+'?mode=ro',uri=True)
            try:
                with closing(sqlite3.connect(path)) as target:source.backup(target)
            finally:source.close()
        database=Database(path)
        saved=database.load()
        g=Game(database,real_now=saved['meta']['real_anchor'] if saved else 1800000000,seed=42)
        app=ManorApp(g,settings_path=Path(folder)/'ui.json',scene_mode='2d')
        try:
            meshes=[];lookup={}
            def mesh_id(mesh):
                if mesh not in lookup:
                    lookup[mesh]=len(meshes)
                    meshes.append(base64.b64encode(mesh.data.tobytes()).decode('ascii'))
                return lookup[mesh]
            objects=[dict(mesh=mesh_id(m),pos=p,angle=a,scale=s,key=k) for m,p,a,s,k in World3D().build_static(g,app.courtyard)]
            prototypes={}
            for frame in range(4):
                prototypes[f'person-{frame}']=mesh_id(person_mesh('native-resident',frame))
            for kind in ('car','bus','truck'):prototypes[kind]=mesh_id(vehicle_mesh(kind,(140,176,167)))
            for kind in decorations.CATALOG:prototypes[f'decoration:{kind}']=mesh_id(decoration_mesh(kind))
            prototypes['decoration:waste']=mesh_id(decoration_mesh('waste'))
            for style in K.ROAD_STYLES:
                for mask in range(16):prototypes[f'road:{style}:{mask}']=mesh_id(road_mesh(style,mask))
            for kind in C.BUILDINGS:
                for level in range(1,6):prototypes[f'manor:{kind}:{level}']=mesh_id(house_mesh(128,70,kind,level))
            for key,spec in K.CATALOG.items():
                for stage in range(4):prototypes[f'town:{key}:{stage}']=mesh_id(house_mesh(spec['footprint'][0]*48,spec['footprint'][1]*48,key,1,stage))
                prototypes[f'town:{key}:4']=mesh_id(house_mesh(spec['footprint'][0]*48,spec['footprint'][1]*48,key))
            for species in C.SPECIES:
                for pose in ('idle','walk','eat','pet','sleep'):
                    for frame in range(4):prototypes[f'{species}:{pose}:{frame}']=mesh_id(pet_mesh(species,0,False,'成年',False,pose,frame))
            road_ids={value for key,value in prototypes.items() if key.startswith('road:')}
            mesh_sources={index:mesh for mesh,index in lookup.items()}
            for record in objects:
                if record['mesh'] in road_ids and record['key'] is None:
                    record['key']=('road',f"{int(record['pos'][0]//48)},{int(record['pos'][1]//48)}")
                source_mesh=mesh_sources[record['mesh']]
                if record['key'] is None and -1 in source_mesh.data[9::10]:
                    record['natural_cell']=(int(record['pos'][0]//48),int(record['pos'][1]//48))
            rules={key:value for key,value in vars(C).items() if key.isupper() and isinstance(value,(str,int,float,bool,dict,list,tuple))}
            rules['CATALOG']=K.CATALOG;rules['ROAD_STYLES']=K.ROAD_STYLES
            for key,value in vars(K).items():
                if key.isupper() and isinstance(value,(str,int,float,bool,dict,list,tuple)):rules[key]=value
            rules['DECORATIONS']=decorations.CATALOG
            cells=[[terrain((x,y)) for x in range(K.GRID_COLS)] for y in range(K.GRID_ROWS)]
            data=dict(version=1,rules=rules,terrain=cells,units_per_meter=12,size=C.WORLD_SIZE,zones=C.TOWN_ZONES,portals=PORTALS,
                      meshes=meshes,objects=objects,prototypes=prototypes,snapshot=g.snapshot())
            target=out/'world.json';temp=target.with_suffix('.tmp')
            temp.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf8');temp.replace(target)
            print(f'Exported {len(meshes)} meshes / {len(objects)} objects / {target.stat().st_size//1024} KiB')
        finally:g.db.close();pygame.quit()


if __name__=='__main__':main()
