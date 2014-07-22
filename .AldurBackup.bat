@echo off
    set OPT=-r- -u- -v -d -O
    goto :Do%1
:Do
    ab 
    goto :eof

:DoSet1
    set X1_DIR=C:\Source\TripeSoft
    set X1_KEY=D:\Source\TripeSoft

    set T2_DIR=
    set T2_KEY=

    set V1_DIR=D:\GLH\TripeSoft
    set V1_KEY=F:\Source\TripeSoft
    goto :eof
