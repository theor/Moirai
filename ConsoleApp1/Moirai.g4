parser grammar Moirai;
options {

  tokenVocab=moirai_lexer;

}
r: (COMMENT | LINE_BREAK)* (action|prop_definition|enum_definition)+ ;

action: (RULE|EVENT) ID SCOPE_OPEN LINE_BREAK effect+ SCOPE_CLOSE LINE_BREAK*;

effect: (set | assign | call) SPACE* LINE_BREAK+;

set: SET  path EQ value;
assign: VAR_ID EQ call;
call : ID  ((expr (COMMA expr)* ));

value: string | path | bool | number | NULL;
expr : value (op value)? ;
op : EQ | NEQ ;

prop_definition: PROP ID EQ ID LINE_BREAK+ ;

enum_definition: ENUM ID EQ ID (COMMA ID)* LINE_BREAK* ;

string: STRING ;

bool: TRUE | FALSE;
path : VAR_ID (DOT ID)* | ID;
number: NUMBER;