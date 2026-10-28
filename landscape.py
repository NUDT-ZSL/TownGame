"""固定位置派生的景观分布；不写入存档、不消耗模型或界面随机流。"""
import random
from functools import lru_cache
from typing import NamedTuple
from appearance import identity_seed
from tree_art import profile
from town_grid import terrain,world_to_cell


class Tree(NamedTuple):
    x:int
    y:int
    species:str
    variant:int
    height:int


@lru_cache(maxsize=1)
def world_trees():
    rng=random.Random(identity_seed('town-layout','landscape'))
    trees=[]
    def group(x,y,count,choices,spread=90):
        for _ in range(count):
            tx=max(44,min(3556,x+rng.randint(-spread,spread)))
            ty=max(170,min(2340,y+rng.randint(-spread//2,spread//2)))
            if terrain(world_to_cell(tx,ty)) not in ('grass','zone'):continue
            if any((tx-t.x)**2+(ty-t.y)**2<72**2 for t in trees):continue
            species=choices[rng.randrange(len(choices))]
            _,variant,height=profile(f'{tx}:{ty}',species)
            trees.append(Tree(tx,ty,species,variant,height))
    for i,x in enumerate(range(170,3500,340)):
        group(x,2280,3 if i%3 else 2,('oak','pine','birch','maple'),100)
    for y in range(200,2200,330):
        group(65,y,2,('birch','cherry','oak'),21)
        group(3530,y+40,2,('pine','maple','oak'),22)
    for x,y in ((175,1215),(1000,1230),(170,1700),(1005,1820),(900,2100)):
        group(x,y,4,('willow','willow','cherry','maple'),90)
    for x,y in ((210,880),(970,870),(2600,820),(3360,820)):
        group(x,y,3,('birch','cherry','oak'),80)
    return tuple(trees)


@lru_cache(maxsize=1)
def estate_trees():
    trees=[]
    for side,x in enumerate((42,1238)):
        for i,y in enumerate((130,245,354,455)):
            seed=identity_seed(f'{side}:{i}','estate-tree')
            species=('cherry','birch','maple','oak','pine','willow')[(i+side*2)%6]
            trees.append(Tree(x+(seed%9-4),y+(seed//11%13-6),species,seed//137%24,82+seed//223%22))
    return tuple(trees)


@lru_cache(maxsize=1)
def ground_details():
    result=[]
    for i,t in enumerate(world_trees()):
        if i%2:continue
        x,y=t.x+(27 if i%4 else -28),t.y+11
        if terrain(world_to_cell(x,y)) in ('grass','zone'):
            result.append((x,y,('shrub','rock','flowers')[i//2%3],i%8))
    return tuple(result)
