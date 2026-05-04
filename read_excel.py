import pandas as pd
df = pd.read_excel('UP Phieu luong WOVO templete.xlsx', header=0, nrows=0)
for i, col in enumerate(df.columns):
    print(f"{i}: {col}")
