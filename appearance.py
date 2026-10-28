"""永久身份派生默认外观，手选样式从存档读取；不使用游戏随机数。"""
import hashlib
from functools import lru_cache


@lru_cache(maxsize=1024)
def identity_seed(identity, kind):
    return int.from_bytes(hashlib.blake2b(f'{kind}:{identity}'.encode('utf-8'),digest_size=8).digest(),'big')


@lru_cache(maxsize=512)
def rabbit_variant(pet_id):
    return identity_seed(pet_id,'rabbit') % (8*4*3*4) if pet_id else 0


COAT_NAMES = ('奶油','雪白','可可','银灰','焦糖','炭灰','燕麦','肉桂')
PATTERN_NAMES = ('纯色','眼罩斑','额头白纹','花斑')
EAR_NAMES = ('立耳','垂耳','单侧垂耳')


def pet_variant(pet_id, meta):
    return meta.get('pet_appearances',{}).get(pet_id,rabbit_variant(pet_id))


def style_groups(species):
    return (('毛色',COAT_NAMES,1),('花纹',PATTERN_NAMES,8),
            ('耳型',EAR_NAMES,32),('额毛',('无额毛','一撮','两撮','三撮'),96)) if species=='兔子' else (
            ('配色',('原色','雪白','可可','银灰','焦糖','炭灰','燕麦','肉桂'),1),
            ('花纹',('纯色','背斑','浅色纹','斑点'),8),
            ('体态',('标准','纤细','圆润'),32),('点缀',('无点缀','领结','徽章','小花'),96))


def style_description(species,variant):
    return ' · '.join(names[variant//step%len(names)] for _,names,step in style_groups(species))


def rabbit_description(pet_id, variant=None):
    variant = rabbit_variant(pet_id) if variant is None else variant
    return f'{COAT_NAMES[variant%8]} · {PATTERN_NAMES[variant//8%4]} · {EAR_NAMES[variant//32%3]}'
