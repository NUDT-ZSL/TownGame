"""扩展城区的固定建筑占地；两种视图与施工保护共享。"""
def landmarks():
    result=[]
    for i,x in enumerate((1420,2120)):
        result.append(('ranch','牧场',(x,2450,230,160),1))
    for row,y in enumerate((960,1260)):
        for col,x in enumerate((3820,4220)):
            result.append(('industry','steam_factory' if row else 'iron_store',(x,y,240,160),1+row))
    for row,y in enumerate((930,1250,1570)):
        for col,x in enumerate((4950,5440)):
            result.append(('city','office' if col else 'glass_home',(x,y,240,170),2+row))
    return result
