# threadProject
 a stock simulation with using Threads, Mutex and Semafor

The MSSQ create table query for project
CREATE TABLE Tarifler (
    TarifID INT IDENTITY(1,1) PRIMARY KEY,
    TarifAdi VARCHAR(255) NOT NULL,
    Kategori VARCHAR(100) NOT NULL,
    HazirlamaSuresi INT NOT NULL,
    Talimatlar TEXT NOT NULL
);

CREATE TABLE Malzemeler (
    MalzemeID INT IDENTITY(1,1) PRIMARY KEY,
    MalzemeAdi VARCHAR(255) NOT NULL,
    ToplamMiktar VARCHAR(50) NOT NULL,
    MalzemeBirim VARCHAR(50) NOT NULL,
    BirimFiyat DECIMAL(10, 2) NOT NULL
);


CREATE TABLE TarifMalzeme (
    TarifID INT NOT NULL,
    MalzemeID INT NOT NULL,
    MalzemeMiktar FLOAT NOT NULL,
    PRIMARY KEY (TarifID, MalzemeID),
    FOREIGN KEY (TarifID) REFERENCES Tarifler(TarifID) 
		ON DELETE CASCADE
        ON UPDATE CASCADE,
    FOREIGN KEY (MalzemeID) REFERENCES Malzemeler(MalzemeID) 
		ON DELETE CASCADE
        ON UPDATE CASCADE
);


ALTER TABLE Tarifler
ADD Maliyet FLOAT;

ALTER TABLE TarifMalzeme
ADD MalzemeMaliyeti DECIMAL(10, 2) NOT NULL DEFAULT 0;


